module ProgressTests

open System
open Whb.Cli
open Whb.Core
open Xunit

let private monotoneNonDecreasing (values: float array) =
    values
    |> Array.pairwise
    |> Array.forall (fun (left, right) -> right + 1e-9 >= left)

[<Fact>]
let ``progress ETA uses reported fraction when available`` () =
    let elapsed = TimeSpan.FromSeconds 20.0
    let status = Progress.snapshot "halfway" (Some 0.5)
    let estimate = Progress.estimateProgress 120.0 elapsed status

    Assert.Equal(0.5, estimate.Fraction, 6)
    Assert.Equal(TimeSpan.FromSeconds 20.0, estimate.Remaining.Value)

[<Fact>]
let ``progress status merge keeps the latest known fraction across description-only updates`` () =
    let current = Progress.snapshot "thermal" (Some 0.42)
    let updated =
        Progress.mergeStatus current
            { Description = "thermal: bypass map point x = 0.010 solved"
              Fraction = None }

    Assert.Equal("thermal: bypass map point x = 0.010 solved", updated.Description)
    Assert.Equal(0.42, updated.Fraction.Value, 6)

[<Fact>]
let ``design bypass maps point subprogress into global progress fraction`` () =
    let caseIn =
        { Defaults.referenceCase with
            Bypass =
                { Defaults.referenceCase.Bypass with
                    Enabled = true
                    Fraction = Some 0.01 } }
    let updates = ResizeArray<DesignRuntime.ProgressUpdate>()
    let point x (reportPointProgress: DesignRuntime.ProgressUpdate -> unit) =
        reportPointProgress (ExecutionProgress.Reporting.step 0.25 "Preparing coupled point solve")
        reportPointProgress (ExecutionProgress.Reporting.step 0.75 "Marching bypass axial profile")
        let mapPoint : DesignBypass.MapPoint =
            { X = x
              TMix = caseIn.Bypass.TargetMixOut
              TTubes = caseIn.Bypass.TargetMixOut - 5.0
              TBp = caseIn.Bypass.TargetMixOut + 5.0
              DpTubes = 1000.0
              DpBpFric = 100.0
              Duty = 1.0e6
              Steam = 1.0
              TLinerMax = caseIn.Bypass.TMixMin
              RhoValve = 1.0
              TValve = caseIn.Bypass.TargetMixOut }
        mapPoint

    DesignBypass.run
        { Case = caseIn
          Mode = "fixed"
          TargetToleranceK = 0.5
          Parallelism = 1
          TotalGasFlow = caseIn.Gas.MassFlow
          MixtureMolarMass = GasProps.mixMolarMass caseIn.Gas.Composition
          LinerArea = Math.PI * caseIn.Bypass.LinerId * caseIn.Bypass.LinerId / 4.0
          Phase = updates.Add
          AcquireWorker = ignore
          ReleaseWorker = ignore
          MapPointAt = point }
    |> ignore

    let fractions =
        updates
        |> Seq.choose (fun update -> update.Fraction)
        |> Seq.toArray

    Assert.Contains(updates, fun update -> update.Description.Contains("Marching bypass axial profile"))
    Assert.True(fractions |> Array.exists (fun value -> value > 0.0 && value < 0.95))
    Assert.True(monotoneNonDecreasing fractions)
