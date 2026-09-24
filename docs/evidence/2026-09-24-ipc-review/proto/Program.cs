// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// IPC review scratch prototype, 2026-09-24. Not product code.
internal static class Program
{
    private static int Main(string[] args) => args.Length == 0 ? 2 : args[0] switch
    {
        "holder" => Holder.Run(args),
        "torn" => Bench.Torn(args),
        "readlat" => Bench.ReadLat(args),
        "walk" => Bench.Walk(args),
        "pipefail" => Bench.PipeFail(args),
        "derive" => Bench.DeriveBench(args),
        "sddl" => Bench.Sddl(args),
        "micro" => Micro.Run(args),
        "censushung" => Micro.CensusHung(args),
        "sleep" => JobReport.Sleep(args),
        "jobreport" => JobReport.Run(args),
        "contender" => Single.Contender(args),
        "single" => Single.Bench(args),
        _ => 2,
    };
}
