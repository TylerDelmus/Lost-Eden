/// <summary>
/// Stock _MaterialManager_t::GetMaterial table (hardcoded).
/// gfxtweak field[9] selects the index; -1 = untextured.
/// </summary>
public static class EffectMaterialTable
{
    public readonly struct Slot
    {
        public readonly string FileName;
        public readonly int Cols;
        public readonly int Rows;
        public readonly int FirstFrame;
        public readonly int LastFrame;

        public Slot(string fileName, int cols, int rows, int firstFrame, int lastFrame)
        {
            FileName = fileName;
            Cols = cols > 0 ? cols : 1;
            Rows = rows > 0 ? rows : 1;
            FirstFrame = firstFrame;
            LastFrame = lastFrame >= firstFrame ? lastFrame : firstFrame;
        }

        public int FrameCount => LastFrame - FirstFrame + 1;
        public bool IsUntextured => string.IsNullOrEmpty(FileName);
    }

    public const int UntexturedIndex = -1;

    static readonly Slot Untextured = new Slot(null, 1, 1, 0, 0);

    // Indexed exactly as stock GetMaterial. Gaps / notes from the walked table.
    static readonly Slot[] Slots =
    {
        /*  0 */ new Slot("advertisement_test.png", 2, 2, 0, 3),
        /*  1 */ new Slot("circle.png", 1, 1, 0, 0),
        /*  2 */ new Slot("Electricify.png", 4, 4, 0, 14),
        /*  3 */ new Slot("explo04_01_v01.png", 8, 8, 0, 30),
        /*  4 */ new Slot("explo05_01_v01.png", 4, 4, 0, 14),
        /*  5 */ new Slot("explo06_01_v01.png", 8, 8, 0, 45),
        /*  6 */ new Slot("hippoexplo01_01_v01.png", 4, 4, 0, 14),
        /*  7 */ new Slot("light_halo.png", 1, 1, 0, 0),
        /*  8 */ new Slot("light_halo2.png", 1, 1, 0, 0),
        /*  9 */ new Slot("mine_explosion02_01_v01.png", 8, 8, 0, 62),
        /* 10 */ new Slot("muzzleflash.png", 8, 8, 0, 52),
        /* 11 */ new Slot("new_explo01_v01.png", 8, 8, 0, 29),
        /* 12 */ new Slot("new_explo05_v01.png", 8, 8, 0, 30),
        /* 13 */ new Slot("noise.PNG", 1, 1, 0, 0),
        /* 14 */ new Slot("rocket_explosion01_v01.png", 8, 8, 0, 30),
        /* 15 */ new Slot("s_bullet.png", 1, 1, 0, 0),
        /* 16 */ new Slot("s_bulletfront.png", 1, 1, 0, 0),
        /* 17 */ new Slot("s_exp1noise.png", 8, 8, 0, 63),
        /* 18 */ new Slot("s_explosion1.png", 8, 8, 0, 63),
        /* 19 */ new Slot("s_explosion2.png", 8, 8, 0, 63),
        /* 20 */ new Slot("s_explosion3.png", 8, 8, 0, 63),
        /* 21 */ new Slot("s_flame.png", 8, 8, 0, 63),
        /* 22 */ new Slot("s_muzzleflash.png", 8, 8, 0, 52),
        /* 23 */ new Slot("s_smokebomb.png", 8, 8, 0, 63),
        /* 24 */ new Slot("s_smokebombblack.png", 8, 8, 0, 63),
        /* 25 */ new Slot("s_smokebombbrown.png", 8, 8, 0, 63),
        /* 26 */ new Slot("shock_stripes.png", 4, 2, 0, 7),
        /* 27 */ new Slot("shock_stripes2.png", 1, 1, 0, 0),
        /* 28 */ new Slot("smoke01_01_v02.png", 4, 4, 8, 15),
        /* 29 */ new Slot("snakeweapon_impact01_01_v01.png", 8, 8, 0, 30),
        /* 30 */ new Slot("swirl.png", 1, 1, 0, 0),
        /* 31 */ new Slot("x_smoke.png", 8, 8, 3, 63),
        /* 32 */ new Slot("thin_smoke.png", 1, 1, 0, 0),
        /* 33 */ new Slot("star_2_ball.png", 8, 2, 0, 15),
        /* 34 */ new Slot("wave_alpha.png", 1, 1, 0, 0),
        /* 35 */ new Slot("watersplash_64x64.png", 1, 1, 0, 0),
        /* 36 */ new Slot("ripple.png", 1, 1, 0, 0),
        /* 37 */ new Slot("ring_64x64.png", 1, 1, 0, 0),
        /* 38 */ new Slot("water_drop.png", 4, 2, 0, 0),
        /* 39 */ new Slot("whitesmoke.png", 8, 8, 3, 63),
        /* 40 */ new Slot("spritefont.png", 1, 1, 0, 0),
        /* 41 */ new Slot("tower_shockwave1.png", 1, 1, 0, 0),
        /* 42 */ new Slot("tower_lightningeffect.png", 4, 2, 0, 7),
        /* 43 */ new Slot("explosion_big.png", 4, 4, 0, 15),
        /* 44 */ new Slot("tower_sidebeam_clan.png", 1, 1, 0, 0),
        /* 45 */ new Slot("tower_sidebeam_omni.png", 1, 1, 0, 0),
        /* 46 */ new Slot("tower_sidebeam_neutral.png", 1, 1, 0, 0),
        /* 47 */ new Slot("vein.png", 1, 1, 0, 0),
        /* 48 */ new Slot("vein2.png", 1, 1, 0, 0),
        /* 49 */ new Slot("mine_explosion02_bw_01_v01.png", 8, 8, 0, 62),
        /* 50 */ new Slot("whitemap8x8.png", 8, 8, 0, 0),
        /* 51 */ new Slot("bubbles_4frames_animation.png", 2, 2, 0, 3),
        /* 52 */ new Slot("alien_beam.png", 1, 1, 0, 0),
        /* 53 */ new Slot("spaceship_engine_trail.png", 1, 1, 0, 0),
        /* 54 */ new Slot("spaceship_engine_trail_colour.png", 1, 1, 0, 0),
        /* 55 */ new Slot("spaceship_engine_trail2.png", 1, 1, 0, 0),
        /* 56 */ new Slot("fog_particle.png", 1, 1, 0, 0),
        /* 57 */ new Slot("Energy_ring.png", 1, 1, 0, 0),
        /* 58 */ new Slot("cloud.png", 1, 1, 0, 0),
        /* 59 */ new Slot("lightning.png", 1, 1, 0, 0),
        /* 60 */ new Slot("muzzle_flash_mg_side.png", 1, 1, 0, 0),
        /* 61 */ new Slot("muzzle_flash_mg_front.png", 1, 1, 0, 0),
        /* 62 */ new Slot("muzzle_flash_blast.png", 1, 1, 0, 0),
        /* 63 */ new Slot("rays.png", 1, 1, 0, 0),
        /* 64 */ new Slot("twirl.png", 1, 1, 0, 0),
        /* 65 */ new Slot("blue_flash_side.png", 1, 1, 0, 0),
        /* 66 */ new Slot("blue_flash_front.png", 1, 1, 0, 0),
        /* 67 */ new Slot("blue_flash_side_upgraded.png", 1, 1, 0, 0),
        /* 68 */ new Slot("blue_flash_front_upgraded.png", 1, 1, 0, 0),
        /* 69 */ new Slot("spark.png", 1, 1, 0, 0),
        /* 70 */ new Slot("mothership_particles.png", 2, 2, 0, 3),
        /* 71 */ new Slot("explosion2.png", 8, 4, 0, 31),
        /* 72 */ new Slot("alien_sprite_01.png", 1, 1, 0, 0),
        /* 73 */ new Slot("flare.png", 1, 1, 0, 0),
        /* 74 */ new Slot("beam.png", 1, 1, 0, 0),
        /* 75 */ new Slot("projectile_ring.png", 1, 1, 0, 0),
        /* 76 */ new Slot("alien_sprite_02.png", 1, 1, 0, 0),
        /* 77 */ new Slot("energy_grid.png", 1, 1, 0, 0),
        /* 78 */ new Slot("effect_lava.png", 1, 1, 0, 0),
        /* 79 */ new Slot("circular_energy.png", 1, 1, 0, 0),
        /* 80 */ new Slot("muzzleflash_1_front.png", 1, 1, 0, 0),
        /* 81 */ new Slot("muzzleflash_1_side.png", 1, 1, 0, 0),
        /* 82 */ new Slot("muzzleflash_2_front.png", 1, 1, 0, 0),
        /* 83 */ new Slot("muzzleflash_2_side.png", 1, 1, 0, 0),
        /* 84 */ new Slot("muzzleflash_3_front.png", 1, 1, 0, 0),
        /* 85 */ new Slot("muzzleflash_3_side.png", 1, 1, 0, 0),
        /* 86 */ new Slot("muzzleflash_4_front.png", 1, 1, 0, 0),
        /* 87 */ new Slot("muzzleflash_4_side.png", 1, 1, 0, 0),
        /* 88 */ new Slot("muzzleflash_5_front.png", 1, 1, 0, 0),
        /* 89 */ new Slot("muzzleflash_5_side.png", 1, 1, 0, 0),
        /* 90 */ new Slot("flame.png", 8, 2, 0, 13),
        /* 91 */ new Slot("dots.png", 1, 1, 0, 0),
        /* 92 */ new Slot("plume.png", 1, 1, 0, 0),
        /* 93 */ new Slot("whoopwhite.png", 1, 1, 0, 0),
        /* 94 */ new Slot("exp_particle.png", 1, 1, 0, 0),
        /* 95 */ new Slot("groundhit.png", 1, 1, 0, 0),
        /* 96 */ new Slot("cord1.png", 1, 1, 0, 0),
        /* 97 */ new Slot("cloud2.png", 1, 1, 0, 0),
        /* 98 */ new Slot("the_wave.png", 1, 1, 0, 0),
        /* 99 */ new Slot("the_wave_fractal.png", 1, 1, 0, 0),
        /*100 */ new Slot("xan_grid.png", 1, 1, 0, 0),
        /*101 */ new Slot("fear_overhead.png", 1, 1, 0, 0),
    };

    public static int SlotCount => Slots.Length;

    public static bool TryGet(int index, out Slot slot)
    {
        if (index == UntexturedIndex)
        {
            slot = Untextured;
            return true;
        }

        if (index < 0 || index >= Slots.Length)
        {
            slot = default;
            return false;
        }

        slot = Slots[index];
        return true;
    }

    public static Slot Get(int index)
    {
        if (TryGet(index, out Slot slot))
            return slot;
        // Last-resort known textured slot (muzzleflash).
        return Slots[10];
    }
}
