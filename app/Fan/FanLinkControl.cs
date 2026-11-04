namespace GHelper.Fan
{
    // Makes the GPU fan run at the same speed as the CPU fan (one curve, one temperature).
    //
    // The EC runs each fan as its own closed loop on its own sensor and ignores the
    // fan-range endpoint (0x00110023) on this model, so the only channel that works is
    // the GPU fan curve itself: we rewrite it as a flat table where every output point
    // equals one duty value, so whatever the GPU sensor reads the EC lands on that duty.
    //
    // Duty cannot be computed from the CPU curve (the EC applies its own hysteresis, so
    // curve value != real fan speed) and cannot be converted by the calibrated fan_max
    // either (measured: 64% duty gave 5000RPM, i.e. the real full scale is ~78, not 72).
    // So this is a closed loop: read both fans, nudge the duty one small step at a time
    // until the GPU reading matches the CPU reading.
    public static class FanLinkControl
    {
        const int INTERVAL = 3000;   // sampling period

        static readonly byte[] temps = { 30, 40, 50, 60, 70, 80, 90, 100 };

        static System.Timers.Timer? timer;
        static int lastPct = -1;

        public static bool Enabled => AppConfig.Is("fan_link_gpu");

        // Called when something else (AutoFans on a mode/power change) has rewritten the GPU
        // curve, so the next tick repaints it even if the fan speeds did not move.
        public static void Invalidate()
        {
            lastPct = -1;
            if (timer is not null && timer.Enabled) Tick();
        }

        public static void Apply()
        {
            if (timer is null)
            {
                timer = new System.Timers.Timer(INTERVAL);
                timer.Elapsed += (_, _) => Tick();
            }
            timer.Enabled = true;
            lastPct = -1;
            Tick();
        }

        public static void Stop()
        {
            if (timer is not null) timer.Enabled = false;
            Logger.WriteLine("FanLink: disabled, GPU curve restored");
            Restore();
        }

        // Hands the GPU fan back to its own curve
        public static void Restore()
        {
            lastPct = -1;
            try
            {
                byte[] gpuCurve = AppConfig.GetFanConfig(AsusFan.GPU);
                if (gpuCurve.Length == 16)
                    Program.acpi?.SetFanCurve(AsusFan.GPU, gpuCurve);
            }
            catch { }
        }

        static void Tick()
        {
            try
            {
                if (!Enabled) { lastPct = -1; return; }
                if (Program.acpi is null) return;

                int cpuFan = Program.acpi.GetFan(AsusFan.CPU);
                int gpuFan = Program.acpi.GetFan(AsusFan.GPU);

                int pct;

                if (cpuFan < 0)
                {
                    // GetFan reports -1 both for a fully stopped fan and for a failed read.
                    // Only treat it as stopped when the CPU is cool, otherwise keep the last value.
                    float? cpuTemp = HardwareControl.GetCPUTemp();
                    if (cpuTemp is null || cpuTemp > 60) return;
                    pct = 0;
                }
                else
                {
                    // Open loop. A closed loop is not usable here: the EC ramps the fan over
                    // several seconds, so the reading always lags the last duty we wrote and
                    // the loop diverges.
                    // The EC's duty->RPM response is not linear either: measured 34% -> ~3850,
                    // 39% -> ~4000, 47% -> ~4450, 48% -> ~4400, i.e. the effective full scale
                    // shrinks as the fan gets faster. Compensate with an affine term whose
                    // intercept is tunable via "fan_link_full" (default 138).
                    double full = AppConfig.Get("fan_link_full", 138) - cpuFan;
                    full = Math.Clamp(full, 80, 130);
                    pct = (int)Math.Round(cpuFan * 100 / full);
                }

                pct = Math.Clamp(pct, 0, 100);

                // Safety valve: a hot GPU must still be able to spin its fan up, otherwise
                // a CPU-idle / GPU-loaded workload would cook the GPU.
                float? gpuTemp = HardwareControl.GetGPUTemp();
                if (gpuTemp is not null && gpuTemp > 0)
                {
                    if (gpuTemp > 85) pct = Math.Max(pct, 80);
                    else if (gpuTemp > 75) pct = Math.Max(pct, 50);
                }

                if (pct == lastPct) return;

                lastPct = pct;

                int result = Program.acpi.SetFanCurve(AsusFan.GPU, FlatCurve(pct));
                Logger.WriteLine($"FanLink: CPU {cpuFan} / GPU {gpuFan} -> duty {pct}% [{(result == 1 ? "OK" : "FAIL " + result)}]");
            }
            catch { }
        }

        // Every output point equals pct, so the EC produces pct for any GPU temperature
        static byte[] FlatCurve(int pct)
        {
            byte[] curve = new byte[16];
            for (int i = 0; i < 8; i++)
            {
                curve[i] = temps[i];
                curve[8 + i] = (byte)Math.Clamp(pct, 0, 100);
            }
            return curve;
        }
    }
}
