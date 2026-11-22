using System.Collections;
using System.IO;
using H1xl.Shared;
using Path = System.IO.Path;
using RoR2;
using RoR2.Artifacts;
using RoR2.Orbs;
using UnityEngine;

namespace AutoLootDistribution
{
    /// <summary>Command cube icon shown while a cube flies to its player. Values are sent over the network: don't reorder.</summary>
    public enum CubeIcon
    {
        None, White, Green, Red, Yellow, Lunar, Equipment, Hallowed, Hex, Page,
        VoidWhite, VoidGreen, VoidRed, VoidYellow,
    }

    /// <summary>
    /// Cube icons for flying command cubes. The host marks the orb effect (fields the effect doesn't use); players who
    /// have the mod replace the Scrap / Fuel Array icon with a rendered cube, everyone else keeps seeing the vanilla icon.
    /// </summary>
    internal static class CubeIcons
    {
        // EffectData.start.x of a marked effect; start.y holds the CubeIcon. Any value far from real positions works.
        public const float Marker = 7331001f;

        private static readonly string[] FileNames =
        {
            null, "white", "green", "red", "yellow", "lunar", "equipment", "hallowed", "hex", "page",
            "void_white", "void_green", "void_red", "void_yellow",
        };

        private static readonly Texture2D[] textures = new Texture2D[FileNames.Length];
        private static readonly Sprite[] sprites = new Sprite[FileNames.Length];

        public static void Load(string pluginFolder)
        {
            // Mod managers may flatten or keep subfolders, so look in the plugin folder and everything below it.
            var found = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(pluginFolder, "cube_*.png", SearchOption.AllDirectories))
                if (!found.ContainsKey(Path.GetFileName(file))) found[Path.GetFileName(file)] = file;

            int loaded = 0;
            for (int i = 1; i < FileNames.Length; i++)
            {
                if (!found.TryGetValue("cube_" + FileNames[i] + ".png", out string path)) continue;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                if (!tex.LoadImage(File.ReadAllBytes(path))) continue;
                textures[i] = tex;
                loaded++;
            }
            if (loaded < FileNames.Length - 1)
                Log.Warn($"Command cube icons: {loaded} of {FileNames.Length - 1} loaded from {pluginFolder}; missing ones keep the vanilla icon.");
        }

        /// <summary>Host side: the cube icon for a dropped pickup.</summary>
        public static CubeIcon For(PickupDef def)
        {
            if (def.equipmentIndex != EquipmentIndex.None)
            {
                EquipmentDef eq = EquipmentCatalog.GetEquipmentDef(def.equipmentIndex);
                return eq && eq.isLunar ? CubeIcon.Lunar : CubeIcon.Equipment;
            }
            switch (def.itemTier)
            {
                case ItemTier.Tier1: return CubeIcon.White;
                case ItemTier.Tier2: return CubeIcon.Green;
                case ItemTier.Tier3: return CubeIcon.Red;
                case ItemTier.Boss: return CubeIcon.Yellow;
                case ItemTier.Lunar: return CubeIcon.Lunar;
                case ItemTier.FoodTier: return CubeIcon.Equipment; // food uses the equipment colour in the game
                case ItemTier.Cursed: return CubeIcon.Hallowed;
                case ItemTier.Hex: return CubeIcon.Hex;
                case ItemTier.Page: return CubeIcon.Page;
                case ItemTier.VoidTier1: return CubeIcon.VoidWhite;
                case ItemTier.VoidTier2: return CubeIcon.VoidGreen;
                case ItemTier.VoidTier3: return CubeIcon.VoidRed;
                case ItemTier.VoidBoss: return CubeIcon.VoidYellow;
                default: return CubeIcon.None;
            }
        }

        public static void Mark(EffectData data, CubeIcon icon)
        {
            data.genericBool = true;
            data.start = new Vector3(Marker, (float)icon, 0f);
        }

        public static CubeIcon Read(EffectData data)
        {
            if (data == null || !data.genericBool || data.start.x != Marker) return CubeIcon.None;
            int i = Mathf.RoundToInt(data.start.y);
            return i > 0 && i < FileNames.Length ? (CubeIcon)i : CubeIcon.None;
        }

        /// <summary>The cube sprite, sized like <paramref name="like"/> (the vanilla icon it replaces).</summary>
        public static Sprite GetSprite(CubeIcon icon, Sprite like)
        {
            int i = (int)icon;
            Texture2D tex = textures[i];
            if (!tex) return null;
            if (!sprites[i])
            {
                float worldWidth = like ? like.bounds.size.x : 1f;
                sprites[i] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width / worldWidth);
            }
            return sprites[i];
        }

        public static Color ColorOf(CubeIcon icon)
        {
            switch (icon)
            {
                case CubeIcon.White: return ColorCatalog.GetColor(ColorCatalog.ColorIndex.Tier1Item);
                case CubeIcon.Green: return ColorCatalog.GetColor(ColorCatalog.ColorIndex.Tier2Item);
                case CubeIcon.Red: return ColorCatalog.GetColor(ColorCatalog.ColorIndex.Tier3Item);
                case CubeIcon.Yellow: return ColorCatalog.GetColor(ColorCatalog.ColorIndex.BossItem);
                case CubeIcon.Lunar: return ColorCatalog.GetColor(ColorCatalog.ColorIndex.LunarItem);
                case CubeIcon.Equipment: return ColorCatalog.GetColor(ColorCatalog.ColorIndex.Equipment);
                case CubeIcon.Hallowed: return ColorCatalog.GetColor(ColorCatalog.ColorIndex.CursedItem);
                case CubeIcon.Hex: return ColorCatalog.GetColor(ColorCatalog.ColorIndex.HexedItem);
                case CubeIcon.Page: return ColorCatalog.GetColor(ColorCatalog.ColorIndex.KeyItem);
                default: return ColorCatalog.GetColor(ColorCatalog.ColorIndex.VoidItem);
            }
        }

        public static bool CommandEnabled => RunArtifactManager.instance && CommandArtifactManager.IsCommandArtifactEnabled;
    }

    /// <summary>
    /// Added to the game's pickup orb effect (pooled objects reuse it). On every activation it checks the host's mark and,
    /// with the Artifact of Command on, swaps the icon for the cube and recolours the trail.
    /// </summary>
    internal sealed class CubeIconSwapper : MonoBehaviour
    {
        private PickupTakenOrbEffect effect;
        private EffectComponent effectComponent;
        private Color trailStart, trailEnd;

        public static void Attach(PickupTakenOrbEffect effect)
        {
            if (!effect.TryGetComponent(out CubeIconSwapper swapper))
            {
                swapper = effect.gameObject.AddComponent<CubeIconSwapper>();
                swapper.effect = effect;
                swapper.effectComponent = effect.GetComponent<EffectComponent>();
                // Captured before the vanilla effect tints them (it does that one frame after enabling).
                if (effect.trailToColor) { swapper.trailStart = effect.trailToColor.startColor; swapper.trailEnd = effect.trailToColor.endColor; }
            }
            // No coroutine for every vanilla orb when no cube icon can be shown (the component above stays: its trail
            // colours must be captured on the first activation).
            if (!Settings.CommandIcons || !CubeIcons.CommandEnabled) return;
            swapper.StartCoroutine(swapper.Run());
        }

        private IEnumerator Run()
        {
            // The effect data is assigned and the vanilla icon setup (one frame after enabling) has run.
            yield return null;
            yield return null;
            CubeIcon icon = CubeIcons.Read(effectComponent ? effectComponent.effectData : null);
            if (icon == CubeIcon.None || !Settings.CommandIcons || !CubeIcons.CommandEnabled) yield break;
            SpriteRenderer renderer = effect.iconSpriteRenderer;
            if (!renderer) yield break;

            // The vanilla icon may still be loading; wait for it briefly so the cube gets the same size.
            for (int i = 0; i < 30 && !renderer.sprite; i++) yield return null;
            Sprite sprite = CubeIcons.GetSprite(icon, renderer.sprite);
            if (!sprite) yield break;
            renderer.sprite = sprite;
            if (Log.Verbose) Log.Info($"Command cube icon shown: {icon}");

            Color color = CubeIcons.ColorOf(icon);
            if (effect.trailToColor)
            {
                effect.trailToColor.startColor = trailStart * color;
                effect.trailToColor.endColor = trailEnd * color;
            }
            foreach (ParticleSystem ps in effect.particlesToColor)
            {
                if (!ps) continue;
                ParticleSystem.MainModule main = ps.main;
                main.startColor = color;
            }
            foreach (SpriteRenderer sr in effect.spritesToColor)
                if (sr) sr.color = color;

            // A late vanilla icon load may still replace the sprite; keep the cube until the effect ends (disabling stops this).
            while (true)
            {
                if (renderer.sprite != sprite) renderer.sprite = sprite;
                yield return null;
            }
        }
    }
}
