using System;
using System.Collections.Generic;
using AOSharp.Common.GameData;
using N3Lite.Surfaces;
using Reflex.Core;
using Reflex.Injectors;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using UnityEngine;

public class Playfield : MonoBehaviour
{
    readonly Dictionary<Identity, Dynel> _dynels = new();

    Transform _dynelsRoot;
    Character _characterPrefab;
    Container _container;
    ISurface _collisionSurface;
    Transform _surfaceRoot;

    public event Action<Dynel> DynelSpawned;
    public event Action<Dynel> DynelDespawned;

    public void Init(int playfieldId, Character characterPrefab, Container container)
    {
        _characterPrefab = characterPrefab;
        _container = container;
        _dynelsRoot = new GameObject("Dynels").transform;
        _dynelsRoot.SetParent(transform, false);
        gameObject.name = $"Playfield_{playfieldId}";
    }

    /// <summary>
    /// The world every character in this playfield collides against — the heightmap, as
    /// <c>DummyVehicle_t::GetSurface</c> resolves it from the playfield in stock
    /// (<c>N3.dll 100011e0</c>). Characters spawned before this is set free-fall, which is stock's
    /// own behaviour with a null surface.
    /// </summary>
    public void SetCollisionSurface(ISurface surface, Transform surfaceRoot)
    {
        _collisionSurface = surface;

        // Publish it for the queries that have no route to the Playfield: the camera's occlusion probe,
        // dynel line-of-sight and the VFX ground lookups. Stock runs all three through Surface_i too.
        LostEden.Vehicles.WorldCollision.Bind(surface, surfaceRoot);
        _surfaceRoot = surfaceRoot;

        foreach (Dynel dynel in _dynels.Values)
            ApplyCollisionSurface(dynel as Character);
    }

    void ApplyCollisionSurface(Character character)
    {
        if (character == null || _collisionSurface == null || character.Motor == null)
            return;

        character.Motor.SetSurface(_collisionSurface, _surfaceRoot);
    }

    public void SpawnDynel(SimpleCharFullUpdateMessage msg)
    {
        if (_characterPrefab == null)
        {
            Debug.LogError("Playfield: Character prefab is not assigned.");
            return;
        }

        if (_dynels.TryGetValue(msg.Identity, out Dynel existing))
        {
            Debug.Log($"[Playfield] Dynel updated: {msg.Identity.Type}:{msg.Identity.Instance} \"{msg.Name}\"");
            existing.Apply(msg);
            if (existing is Character existingCharacter)
            {
                ApplyCollisionSurface(existingCharacter);
                existingCharacter.Motor.RequestSurfacePriorityForSpawn(existingCharacter.transform.position);
            }
            DynelSpawned?.Invoke(existing);
            return;
        }

        Character character = Instantiate(_characterPrefab, _dynelsRoot);
        try
        {
            GameObjectInjector.InjectObject(character.gameObject, _container);
            character.Initialize(msg);
            _dynels[msg.Identity] = character;
            ApplyCollisionSurface(character);
            character.Motor.RequestSurfacePriorityForSpawn(character.transform.position);
            Debug.Log($"[Playfield] Dynel spawned: {msg.Identity.Type}:{msg.Identity.Instance} \"{msg.Name}\" @ ({msg.Position.X:F1}, {msg.Position.Y:F1}, {msg.Position.Z:F1}) (total={_dynels.Count})");
            DynelSpawned?.Invoke(character);
        }
        catch (Exception ex)
        {
            Destroy(character.gameObject);
            Debug.LogError($"[Playfield] Dynel spawn failed for {msg.Identity.Type}:{msg.Identity.Instance} \"{msg.Name}\": {ex}");
        }
    }

    public bool TryGetDynel(Identity identity, out Dynel dynel) => _dynels.TryGetValue(identity, out dynel);

    public bool TryGetCharacter(Identity identity, out Character character)
    {
        character = null;
        if (!_dynels.TryGetValue(identity, out Dynel dynel) || dynel is not Character c)
            return false;

        character = c;
        return true;
    }

    public void ApplyStat(StatMessage msg)
    {
        if (!_dynels.TryGetValue(msg.Identity, out Dynel dynel))
            return;

        dynel.Apply(msg);
    }

    public void ApplyFullCharacter(FullCharacterMessage msg)
    {
        if (!_dynels.TryGetValue(msg.Identity, out Dynel dynel))
            return;

        dynel.Apply(msg);
    }

    public void ApplyCharDCMove(CharDCMoveMessage msg)
    {
        if (!_dynels.TryGetValue(msg.Identity, out Dynel dynel))
            return;

        if (dynel is Character character)
            character.Apply(msg);
    }

    public void ApplyCharacterAction(CharacterActionMessage msg)
    {
        if (!_dynels.TryGetValue(msg.Identity, out Dynel dynel))
            return;

        if (dynel is not Character character)
            return;

        int action = (int)msg.Action;
        if (action == AnimKindIds.FightEnterAction)
        {
            Character target = null;
            if (msg.Target.Instance != 0)
                TryGetCharacter(msg.Target, out target);
            character.ApplyFightEnter(target);
            return;
        }

        if (action == AnimKindIds.FightLeaveAction)
        {
            character.ApplyFightLeave();
            return;
        }

        character.Apply(msg);
    }

    public void ApplyAttackInfo(AttackInfoMessage msg)
    {
        if (msg == null || !_dynels.TryGetValue(msg.Identity, out Dynel dynel) || dynel is not Character attacker)
            return;

        attacker.PlayAttackerSwingAnim((int)msg.WeaponSlot);
    }

    public void ApplyAttack(AttackMessage msg)
    {
        if (msg == null || !_dynels.TryGetValue(msg.Identity, out Dynel dynel) || dynel is not Character attacker)
            return;

        Character target = null;
        if (msg.Target.Instance != 0)
            TryGetCharacter(msg.Target, out target);
        attacker.ApplyFightEnter(target);
    }

    public void ApplyStopFight(StopFightMessage msg)
    {
        if (msg == null || !_dynels.TryGetValue(msg.Identity, out Dynel dynel) || dynel is not Character character)
            return;

        character.ApplyFightLeave();
    }

    public void ApplyFollowTarget(FollowTargetMessage msg)
    {
        if (!_dynels.TryGetValue(msg.Identity, out Dynel dynel))
            return;

        if (dynel is Character character)
            character.Apply(msg);
    }

    public void ApplyAppearanceUpdate(AppearanceUpdateMessage msg)
    {
        if (!_dynels.TryGetValue(msg.Identity, out Dynel dynel))
            return;

        if (dynel is Character character)
            character.Apply(msg);
    }

    public void ApplyHealthDamage(HealthDamageMessage msg)
    {
        // Target is the damaged dynel (same as AttackInfo); Identity is typically the source.
        Identity victim = msg.Target.Instance != 0 ? msg.Target : msg.Identity;
        if (!_dynels.TryGetValue(victim, out Dynel dynel))
            return;

        dynel.Stats.Set(Stat.Health, msg.TargetHp);
    }

    public void DespawnDynel(Identity identity)
    {
        if (!_dynels.TryGetValue(identity, out Dynel dynel))
            return;

        _dynels.Remove(identity);
        DynelDespawned?.Invoke(dynel);
        Destroy(dynel.gameObject);
        Debug.Log($"[Playfield] Dynel despawned: {identity.Type}:{identity.Instance} (total={_dynels.Count})");
    }
}
