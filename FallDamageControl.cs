using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace FallDamageControl;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
internal sealed class FallDamageControl : BaseUnityPlugin
{
    public const string PluginGUID = "ewigl.FallDamageControl";
    public const string PluginName = "Fall Damage Control";
    public const string PluginVersion = "0.0.6";

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(PluginGUID);
        _harmony.PatchAll();
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}

[HarmonyPatch(typeof(Character), "UpdateGroundContact")]
internal static class FallDamagePatch
{
    private const float SafeFallHeight = 4f;
    private const float DamageFallHeight = 16f;
    private const float MaxFallDamage = 100f;

    private static readonly HashSet<int> BlacklistedPrefabHashes = new()
    {
        "Blob".GetStableHashCode(),
        "BlobElite".GetStableHashCode(),
        "BlobTar".GetStableHashCode(),
        "BlobLava".GetStableHashCode()
    };

    private static readonly AccessTools.FieldRef<Character, Vector3> GetGroundContactNormal =
        AccessTools.FieldRefAccess<Character, Vector3>("m_groundContactNormal");

    private static readonly AccessTools.FieldRef<Character, Vector3> GetGroundContactPoint =
        AccessTools.FieldRefAccess<Character, Vector3>("m_groundContactPoint");

    private static readonly AccessTools.FieldRef<Character, float> GetMaxAirAltitude =
        AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");

    private static readonly AccessTools.FieldRef<Character, bool> GetGroundContact =
        AccessTools.FieldRefAccess<Character, bool>("m_groundContact");

    private static readonly AccessTools.FieldRef<Character, ZNetView> GetNView =
        AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

    internal struct CharacterAirData
    {
        public Vector3 LastGroundContactNormal;
        public Vector3 LastGroundContactPoint;
        public float LastMaxAirAltitude;
        public bool GroundContact;
    }

    [HarmonyPrefix]
    private static void Prefix(Character __instance, out CharacterAirData __state)
    {
        __state = default;

        if (__instance == null || __instance.IsPlayer())
            return;

        __state = new CharacterAirData
        {
            LastGroundContactNormal = GetGroundContactNormal(__instance),
            LastGroundContactPoint = GetGroundContactPoint(__instance),
            LastMaxAirAltitude = GetMaxAirAltitude(__instance),
            GroundContact = GetGroundContact(__instance)
        };
    }

    [HarmonyPostfix]
    private static void Postfix(Character __instance, ref CharacterAirData __state)
    {
        if (__instance == null || __instance.IsDead() || __instance.IsPlayer())
            return;

        if (!__state.GroundContact)
            return;

        var znetView = GetNView(__instance);
        var zdo = znetView?.GetZDO();

        if (zdo != null && BlacklistedPrefabHashes.Contains(zdo.GetPrefab()))
            return;

        float fallHeight = Mathf.Max(0f, __state.LastMaxAirAltitude - __instance.transform.position.y);
        if (fallHeight <= SafeFallHeight)
            return;

        var hitData = new HitData
        {
            m_damage = { m_damage = Mathf.Clamp01((fallHeight - SafeFallHeight) / DamageFallHeight) * MaxFallDamage },
            m_point = __state.LastGroundContactPoint,
            m_dir = __state.LastGroundContactNormal
        };

        __instance.Damage(hitData);
    }
}