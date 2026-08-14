namespace Whb.Core.Designers

open Whb.Core
open Whb.Core.Types
open System
open Whb.Core.Constants
open Whb.Core.Optimizer

/// <summary>
/// Provides high-level WHB design orchestration over the thermal, process, vibration, and mechanical calculation modules.
/// </summary>
/// <remarks>
/// Exposes high-level design orchestration for WHB calculations. Results inherit the assumptions and validity limits of the underlying empirical and theoretical correlations.
/// </remarks>
module Designer =

    [<CLIMutable>]
    type DesignBasis =
        { Name: string
          Duty: float
          SteamPressure: float
          GasInletTemperature: float
          GasMassFlow: float
          Constraints: Optimization.Constraint list }

    [<CLIMutable>]
    type DesignCandidate =
        { Name: string
          TubeCount: int
          TubeLength: float
          TubeOuterDiameter: float
          ShellInnerDiameter: float
          Score: float
          Notes: string list }

    /// <summary>
    /// Design variables the search is allowed to move, with the ranges it may move them in.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow: these are the levers a thermal designer actually turns once the
    /// shell is fixed. Every evaluation is a full coupled solve, so widening the set costs
    /// real time.
    /// </remarks>
    let defaultVariables (case: DesignCase) : Optimization.Variable list =
        let ferruleLength =
            case.Ferrule.Lengths |> List.sumBy (fun (frac, l) -> frac * l)
        [ { Name = "lunghezza ferrula"
            Current = ferruleLength * 1000.0
            Lower = 100.0
            Upper = 800.0
            Step = 50.0
            Unit = "mm" }
          { Name = "lunghezza tubi"
            Current = case.Tube.Length
            Lower = case.Tube.Length * 0.8
            Upper = case.Tube.Length * 1.2
            Step = case.Tube.Length * 0.05
            Unit = "m" } ]

    /// Applies a variable vector to a case. Order matches `defaultVariables`.
    let applyVariables (case: DesignCase) (values: float[]) =
        let ferruleMm = if values.Length > 0 then values.[0] else nan
        let tubeLen = if values.Length > 1 then values.[1] else nan
        let withFerrule =
            if Double.IsNaN ferruleMm then case
            else
                let total = case.Ferrule.Lengths |> List.sumBy fst
                let scale = if total > 0.0 then 1.0 / total else 1.0
                { case with
                    Ferrule =
                        { case.Ferrule with
                            Lengths = case.Ferrule.Lengths |> List.map (fun (f, _) -> (f * scale, ferruleMm / 1000.0)) } }
        if Double.IsNaN tubeLen then withFerrule
        else { withFerrule with Tube = { withFerrule.Tube with Length = tubeLen } }

    /// <summary>
    /// Reads the four default constraints off a finished design result.
    /// </summary>
    /// <remarks>
    /// Order matches <see cref="Optimization.defaultConstraints"/>. Values are in the units
    /// declared there, which are the units an engineer writes the criterion in - degrees
    /// Celsius and millibar, not kelvin and pascal.
    /// </remarks>
    let readConstraints (r: DesignResult) =
        let hot = r.Cells |> List.filter (fun c -> not c.InFerrule)
        let dnbr = if hot.IsEmpty then nan else hot |> List.map (fun c -> c.DNBR) |> List.min
        let tMetal =
            if r.Cells.IsEmpty then nan
            else kToC (r.Cells |> List.map (fun c -> c.TMetalIn) |> List.max)
        let fiv =
            if r.Vibration.IsEmpty then 0.0
            else r.Vibration |> List.map (fun v -> v.FeiRatio) |> List.max
        [| dnbr; tMetal; r.DpGas / 100.0; fiv |]

    /// <summary>
    /// Searches the design variables for the largest duty that still satisfies every
    /// constraint, and reports what holds the answer in place.
    /// </summary>
    /// <remarks>
    /// The objective is the negated duty, because the search minimises. What matters as much
    /// as the number is <see cref="Optimization.Result.Kind"/>: an optimum sitting on an
    /// active constraint means the constraint is the design, while one sitting on a search
    /// bound means only that the range was drawn too small.
    /// </remarks>
    let optimize (runDesign: DesignCase -> DesignResult) (case: DesignCase)
                 (problem: Optimization.OptimizationProblem) =
        let evaluate (values: float[]) =
            let candidate = applyVariables case values
            let r = runDesign candidate
            (-r.Duty, readConstraints r)
        Optimization.solve problem evaluate

    /// Default problem: maximise duty within the four standard limits.
    let defaultProblem (case: DesignCase) : Optimization.OptimizationProblem =
        { Name = sprintf "Massimizzazione della potenza - %s" case.Name
          Variables = defaultVariables case
          Constraints = Optimization.defaultConstraints
          Objective = "massima potenza scambiata nel rispetto dei vincoli"
          MaxIterations = 40
          Tolerance = 1e-2 }
