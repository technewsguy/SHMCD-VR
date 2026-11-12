// Time-flow fixes for VR.
using HarmonyLib;
using UnityEngine;

namespace SHMCDVR
{
    // PlayerController.UpdateMovement (in Update) sets its jump time shifter to 0.75 whenever the player is not grounded.
    // At level start the player spawns slightly above the floor and drops ('LandingPunch'), so time runs fast as if you
    // were moving. Only keep it after a real jump. Done here in LateUpdate (before TimeControl.LateUpdate reads the
    // shifters) because Harmony-patching PlayerController methods hangs Mono at startup (like PlayerStats).
    // Also, debug: for 5 s after each level load, log what is driving time.
    [DefaultExecutionOrder(-32000)]
    public class TimeStartLog : MonoBehaviour
    {
        float until = -1f, next;
        static readonly System.Reflection.FieldInfo LastJump = AccessTools.Field(typeof(PlayerController), "lastJumpTime");
        static readonly System.Reflection.FieldInfo JumpShifter = AccessTools.Field(typeof(PlayerController), "_jumpTimeShifter");

        void LateUpdate()
        {
            // Level intros run scripted full-speed time (LevelTimeline.Timeshift -> SimplifiedTimeShifter amount 1 for N s
            // of GAME time; the script's waits are game time too). Capping the amount slows the intro down without
            // breaking its timing. Only strong shifters in the first seconds after a load, so punches/throws are untouched.
            if (Plugin.VR && Time.unscaledTime < introUntil && TimeControl.Instance != null)
            {
                float cap = Plugin.IntroTimeScale.Value;
                foreach (var sh in TimeControl._timeShifterRegistry)
                {
                    SimplifiedTimeShifter st = sh as SimplifiedTimeShifter;
                    if (st != null && st.timeShiftAmount > cap && st.timeShiftAmount >= 0.9f) st.timeShiftAmount = cap;
                }
            }
            PlayerController pc = PlayerController.Instance;
            if (!Plugin.VR || !Plugin.NoFallTime.Value || pc == null || LastJump == null || JumpShifter == null) return;
            if (Time.time - (float)LastJump.GetValue(pc) <= 3f) return;
            InstantTimeShifter s = JumpShifter.GetValue(pc) as InstantTimeShifter;
            if (s != null && s.CurrentTimeModifier > 0f) s.CurrentTimeModifier = 0f;
        }

        float introUntil = -1f;

        void Start() { UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => { until = Time.unscaledTime + 5f; introUntil = Time.unscaledTime + 10f; }; }

        void Update()
        {
            if (!Plugin.LogEffects.Value || Time.unscaledTime > until || Time.unscaledTime < next || TimeControl.Instance == null) return;
            next = Time.unscaledTime + 0.25f;
            string parts = "";
            foreach (var sh in TimeControl._timeShifterRegistry)
                if (sh != null && sh.CurrentTimeModifier > 0.05f) parts += " " + sh.GetType().Name + "=" + sh.CurrentTimeModifier.ToString("F2");
            Plugin.Log.LogInfo("[time] start timeScale=" + Time.timeScale.ToString("F2") + " forced=" + TimeControl.forcedTimeScale.ToString("F2") + parts);
        }
    }
}
