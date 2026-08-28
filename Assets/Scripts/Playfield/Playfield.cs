using System;
using System.Collections.Generic;
using AOSharp.Common.GameData;
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

    public void Init(int playfieldId, Character characterPrefab, Container container)
    {
        _characterPrefab = characterPrefab;
        _container = container;
        _dynelsRoot = new GameObject("Dynels").transform;
        _dynelsRoot.SetParent(transform, false);
        gameObject.name = $"Playfield_{playfieldId}";
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
            return;
        }

        Character character = Instantiate(_characterPrefab, _dynelsRoot);
        try
        {
            GameObjectInjector.InjectObject(character.gameObject, _container);
            character.Initialize(msg);
            _dynels[msg.Identity] = character;
            Debug.Log($"[Playfield] Dynel spawned: {msg.Identity.Type}:{msg.Identity.Instance} \"{msg.Name}\" @ ({msg.Position.X:F1}, {msg.Position.Y:F1}, {msg.Position.Z:F1}) (total={_dynels.Count})");
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

    public void DespawnDynel(Identity identity)
    {
        if (!_dynels.TryGetValue(identity, out Dynel dynel))
            return;

        _dynels.Remove(identity);
        Destroy(dynel.gameObject);
        Debug.Log($"[Playfield] Dynel despawned: {identity.Type}:{identity.Instance} (total={_dynels.Count})");
    }
}
