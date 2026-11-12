// [Gameplay] OneHitKills: like SUPERHOT VR, a punch or melee hit takes an enemy down at once.
// The game normally needs a stun first (PejAiBodyPart.TakeDownAttackReaction only damages a stunned enemy, and
// melee chunks may be in "Stun" damage mode). Bosses, armour and bullet-proof parts keep their own rules.
using HarmonyLib;
using UnityEngine;

namespace SHMCDVR
{
    [HarmonyPatch(typeof(PejAiBodyPart), "TakeDownAttackReaction")]
    static class OneHitPunch
    {
        static readonly System.Reflection.FieldInfo Body = AccessTools.Field(typeof(PejAiBodyPart), "_body");
        static readonly System.Reflection.FieldInfo StunTimer = AccessTools.Field(typeof(PejAiController), "StunTimer");
        static readonly System.Reflection.FieldInfo StrongStun = AccessTools.Field(typeof(PejAiController), "strongStun");

        static void Prefix(PejAiBodyPart __instance)
        {
            if (!Plugin.OneHitKills.Value || Body == null || StunTimer == null || StrongStun == null) return;
            PejAiBody body = Body.GetValue(__instance) as PejAiBody;
            if (body == null || body.controller == null || body.controller.IsDead || body.controller.enemyModManager.IsBoss()) return;
            if ((float)StunTimer.GetValue(body.controller) <= 0f) StunTimer.SetValue(body.controller, 0.5f);   // counts as stunned
            StrongStun.SetValue(body.controller, true);                                                         // next damage is lethal
        }
    }

    [HarmonyPatch(typeof(PejAiBodyPart), "MeleeeAttackReaction")]
    static class OneHitMelee
    {
        static readonly System.Reflection.FieldInfo Body = AccessTools.Field(typeof(PejAiBodyPart), "_body");
        static readonly System.Reflection.FieldInfo BulletProof = AccessTools.Field(typeof(PejAiBodyPart), "isBulletProof");

        static void Prefix(ref ShardsChunk.DamageMode damageMode)
        {
            if (Plugin.OneHitKills.Value) damageMode = ShardsChunk.DamageMode.Kill;
        }

        // A "kill" hit on an arm or leg only cuts that limb off; finish the enemy too.
        static void Postfix(PejAiBodyPart __instance)
        {
            if (!Plugin.OneHitKills.Value || Body == null) return;
            PejAiBody body = Body.GetValue(__instance) as PejAiBody;
            if (body == null || body.controller == null || body.controller.IsDead || body.controller.enemyModManager.IsBoss()) return;
            if (BulletProof != null && (bool)BulletProof.GetValue(__instance)) return;
            body.Kill(PejAiController.DeathSource.MeleePunch);
            if (Plugin.LogControllerButtons.Value) Plugin.Log.LogInfo("[melee] one-hit kill after limb hit on " + __instance.name);
        }
    }
}
