namespace Whb.Core.Optimizer

open System
module Optimization =

    [<CLIMutable>]
    type Constraint =
        { Name: string; Min: float option; Max: float option; Unit: string; Weight: float }

    [<CLIMutable>]
    type Variable =
        { Name: string; Current: float; Lower: float; Upper: float; Step: float; Unit: string }

    [<CLIMutable>]
    type OptimizationProblem =
        { Name: string
          Variables: Variable list
          Constraints: Constraint list
          Objective: string
          MaxIterations: int
          Tolerance: float }
    let defaultConstraints (minDNBR: float) =
        [ { Name = "DNBR"; Min = Some minDNBR; Max = None; Unit = "-"; Weight = 1.0 }
          { Name = "T metal max"; Min = None; Max = Some 450.0; Unit = "degC"; Weight = 1.0 }
          { Name = "Gas pressure drop"; Min = None; Max = Some 300.0; Unit = "mbar"; Weight = 0.5 }
          { Name = "FIV V/Vcrit"; Min = None; Max = Some 0.8; Unit = "-"; Weight = 1.0 } ]

    /// <summary>
    /// Why a solution sits where it sits.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole point of reporting an optimum. A stationary point inside
    /// the feasible region is a genuine trade-off and moving away from it costs in every
    /// direction. A point held by an active constraint is not a trade-off at all: it is the
    /// constraint, and the way to improve is to relax that constraint. A point sitting on the
    /// edge of the search box is neither - it only says the box was drawn too small.
    /// </remarks>
    type OptimumKind =
        /// Interior stationary point: no constraint active, no variable at a bound.
        | Interior
        /// Held by one or more constraints, named in `ActiveConstraints`.
        | AtConstraint
        /// Held by the search bounds themselves, named in `VariablesAtBound`.
        | AtSearchBound
        /// No point in the search range satisfies every constraint. Nothing is being optimised
        /// here, so the position carries no meaning beyond "least infeasible".
        | NoFeasiblePoint

    /// One evaluated design point.
    type Evaluation =
        { /// Variable values, in the order of `OptimizationProblem.Variables`.
          Values: float[]
          /// Objective value; the search minimises it.
          Objective: float
          /// Constraint readings, in the order of `OptimizationProblem.Constraints`.
          ConstraintValues: float[]
          /// Total violation, zero when every constraint is satisfied.
          Violation: float
          Feasible: bool }

    type Result =
        { Best: Evaluation
          Kind: OptimumKind
          /// Constraints active at the optimum, i.e. sitting on their limit.
          ActiveConstraints: string list
          /// Variables resting on the edge of their search range.
          VariablesAtBound: string list
          Evaluations: int
          /// True when the search stopped because it stopped moving rather than because it ran
          /// out of iterations.
          Converged: bool
          Notes: string list }

    /// Distance by which an evaluation misses a constraint, weighted, zero when satisfied.
    let violationOf (constraints: Constraint list) (values: float[]) =
        constraints
        |> List.mapi (fun i c ->
            let v = if i < values.Length then values.[i] else 0.0
            let scale = max 1e-9 (abs v)
            let lo = match c.Min with Some m when v < m -> (m - v) / scale | _ -> 0.0
            let hi = match c.Max with Some m when v > m -> (v - m) / scale | _ -> 0.0
            max 0.0 c.Weight * (lo + hi))
        |> List.sum

    /// <summary>
    /// Constraints that control the outcome: sitting on their limit, or violated.
    /// </summary>
    /// <remarks>
    /// A violated constraint counts as active. It is what the search is fighting, and naming it
    /// is the whole answer when no feasible point exists.
    /// </remarks>
    let activeConstraints (constraints: Constraint list) (values: float[]) (band: float) =
        constraints
        |> List.mapi (fun i c -> (i, c))
        |> List.filter (fun (i, c) ->
            let v = if i < values.Length then values.[i] else nan
            if Double.IsNaN v then false
            else
                let scale = max 1e-9 (abs v)
                let onMin = match c.Min with Some m -> abs (v - m) / scale <= band || v < m | None -> false
                let onMax = match c.Max with Some m -> abs (v - m) / scale <= band || v > m | None -> false
                onMin || onMax)
        |> List.map (fun (_, c) -> c.Name)

    /// <summary>
    /// Constrained search by coordinate descent with a shrinking step.
    /// </summary>
    /// <remarks>
    /// Each variable is probed up and down in turn; a move is kept when it reduces the
    /// penalised objective. When no coordinate improves, the step is halved and the sweep
    /// repeats, until the step falls below the tolerance or the evaluation budget runs out.
    /// The method is deliberately simple and derivative-free, because every evaluation is a
    /// full design solve: what matters is that it is bounded, deterministic and reports what
    /// held the solution in place, not that it converges in the fewest possible steps.
    /// Infeasible points are ranked by violation first, so the search walks back into the
    /// feasible region before it starts trading objective.
    /// </remarks>
    let solve (problem: OptimizationProblem)
              (evaluate: float[] -> float * float[]) : Result =
        let vars = problem.Variables |> List.toArray
        let n = vars.Length
        if n = 0 then
            let (obj, cv) = evaluate [||]
            { Best = { Values = [||]; Objective = obj; ConstraintValues = cv
                       Violation = violationOf problem.Constraints cv
                       Feasible = violationOf problem.Constraints cv <= 0.0 }
              Kind = Interior
              ActiveConstraints = activeConstraints problem.Constraints cv 1e-3
              VariablesAtBound = []
              Evaluations = 1
              Converged = true
              Notes = [ "Nessuna variabile da ottimizzare: valutato il solo punto corrente." ] }
        else
            let clamp i (v: float) = max vars.[i].Lower (min vars.[i].Upper v)
            let mutable evals = 0
            let evalAt (x: float[]) =
                evals <- evals + 1
                let (obj, cv) = evaluate x
                let viol = violationOf problem.Constraints cv
                { Values = Array.copy x; Objective = obj; ConstraintValues = cv
                  Violation = viol; Feasible = viol <= 0.0 }
            // Feasibility dominates: an infeasible point never beats a feasible one, however
            // good its objective looks.
            let better (a: Evaluation) (b: Evaluation) =
                if a.Violation > 0.0 || b.Violation > 0.0 then a.Violation < b.Violation
                else a.Objective < b.Objective
            let x = vars |> Array.mapi (fun i v -> clamp i v.Current)
            let mutable best = evalAt x
            let mutable steps = vars |> Array.map (fun v -> max 1e-12 v.Step)
            let maxEvals = max 1 problem.MaxIterations
            let tol = max 1e-12 problem.Tolerance
            let mutable converged = false
            let mutable go = true
            while go do
                let mutable improvedSweep = false
                for i in 0 .. n - 1 do
                    if evals < maxEvals then
                        for dir in [| 1.0; -1.0 |] do
                            if evals < maxEvals then
                                let trial = Array.copy best.Values
                                let moved = clamp i (trial.[i] + dir * steps.[i])
                                if abs (moved - trial.[i]) > 1e-15 then
                                    trial.[i] <- moved
                                    let candidate = evalAt trial
                                    if better candidate best then
                                        best <- candidate
                                        improvedSweep <- true
                if not improvedSweep then
                    // Nothing improved at this resolution: refine and try again.
                    steps <- steps |> Array.map (fun s -> s * 0.5)
                    if steps |> Array.forall (fun s -> s <= tol) then
                        converged <- true
                        go <- false
                if evals >= maxEvals then go <- false
            let atBound =
                [ for i in 0 .. n - 1 do
                    let v = best.Values.[i]
                    let span = max 1e-12 (vars.[i].Upper - vars.[i].Lower)
                    if abs (v - vars.[i].Lower) / span <= 1e-6
                       || abs (v - vars.[i].Upper) / span <= 1e-6 then
                        yield vars.[i].Name ]
            let active = activeConstraints problem.Constraints best.ConstraintValues 1e-3
            // Order matters: a constraint holding the point is the useful answer, and a search
            // bound is only worth reporting when nothing else explains the position.
            // Feasibility is checked first: calling an infeasible point an interior optimum
            // would claim a trade-off where there is only a design that does not close.
            let kind =
                if not best.Feasible then NoFeasiblePoint
                elif not (List.isEmpty active) then AtConstraint
                elif not (List.isEmpty atBound) then AtSearchBound
                else Interior
            let notes =
                [ match kind with
                  | Interior ->
                      yield "Ottimo INTERNO: nessun vincolo attivo e nessuna variabile al bordo dell'intervallo di ricerca. E' un vero punto stazionario: allontanarsi peggiora in ogni direzione, e per migliorare serve cambiare il problema, non i limiti."
                  | AtConstraint ->
                      yield sprintf "Ottimo AL VINCOLO: la soluzione e' tenuta ferma da %s. Non e' un compromesso fra effetti opposti: e' il vincolo. Per migliorare occorre rilassare proprio quello." (String.concat ", " active)
                  | AtSearchBound ->
                      yield sprintf "Ottimo AL BORDO DELLA RICERCA su %s: nessun vincolo e' attivo, quindi il valore riflette solo dove e' stato fermato l'intervallo di ricerca. NON e' un limite fisico: allargare l'intervallo e rilanciare." (String.concat ", " atBound)
                  | NoFeasiblePoint ->
                      yield sprintf "NESSUN PUNTO AMMISSIBILE nell'intervallo di ricerca: violazione residua %.3e. Il punto riportato e' il meno inammissibile trovato, non un ottimo, e la sua posizione non va interpretata. Vincoli non soddisfatti: %s." best.Violation (String.concat ", " active)
                      yield "Le strade sono due: allargare l'intervallo delle variabili, oppure prendere atto che con questa geometria i vincoli non sono simultaneamente soddisfacibili e rivederli."

                  if not converged then
                      yield sprintf "Ricerca fermata al tetto di %d valutazioni senza raggiungere la tolleranza sul passo: il punto riportato e' il migliore trovato, non un ottimo dimostrato." maxEvals ]
            { Best = best
              Kind = kind
              ActiveConstraints = active
              VariablesAtBound = atBound
              Evaluations = evals
              Converged = converged
              Notes = notes }
