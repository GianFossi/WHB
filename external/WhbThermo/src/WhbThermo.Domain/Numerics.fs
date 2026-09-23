namespace WhbThermo.Domain

open System
open Ganfoss.ROP

/// Numerical primitives shared by every correlation.
///
/// Two things live here that were previously scattered and inconsistent: integer
/// powers, and bracketed root finding.
///
/// INTEGER POWERS. F#'s `**` always calls Math.Pow, which costs 20 to 40 times a
/// multiplication even when the exponent is 2. A NASA-9 evaluation uses three of
/// them, a Wilke interaction matrix two per pair; a sulfur condenser profile of
/// twenty points reaches roughly 72 000 Math.Pow calls, almost all avoidable.
/// Horner form removes them entirely from the polynomial path.
///
/// ROOT FINDING. Every bisection in the library ran a fixed iteration count and
/// returned whatever it had, converged or not. That is two faults at once:
/// wasted work when it converges early, and a silently wrong answer when it does
/// not. `bisect` stops on tolerance and FAILS when it cannot bracket.
[<AutoOpen>]
module Numerics =

    /// x^2 without Math.Pow.
    let inline sq (x: float) = x * x

    /// x^3 without Math.Pow.
    let inline cube (x: float) = x * x * x

    /// Integer power by repeated multiplication. Falls back to Math.Pow beyond
    /// the range where unrolling pays.
    let inline powi (x: float) (n: int) =
        match n with
        | 0 -> 1.0
        | 1 -> x
        | 2 -> x * x
        | 3 -> x * x * x
        | 4 -> let s = x * x in s * s
        | 5 -> let s = x * x in s * s * x
        | -1 -> 1.0 / x
        | -2 -> 1.0 / (x * x)
        | _ -> Math.Pow(x, float n)

    /// Horner evaluation of a0 + a1 x + a2 x^2 + ... No Math.Pow at all.
    let horner (coefficients: float[]) (x: float) =
        let mutable acc = 0.0
        for i in coefficients.Length - 1 .. -1 .. 0 do
            acc <- acc * x + coefficients.[i]
        acc

    /// NASA-9 Cp/R: a1/T^2 + a2/T + a3 + a4 T + a5 T^2 + a6 T^3 + a7 T^4.
    ///
    /// The positive-power part is Horner; the two negative powers are explicit
    /// divisions. Three Math.Pow calls become none.
    let inline nasa9CpOverR (a: float[]) (t: float) =
        let inv = 1.0 / t
        a.[0] * inv * inv + a.[1] * inv
        + (((((a.[6] * t + a.[5]) * t + a.[4]) * t) + a.[3]) * t + a.[2])

    /// NASA-9 H/(R T).
    let inline nasa9HOverRT (a: float[]) (b0: float) (t: float) =
        let inv = 1.0 / t
        -a.[0] * inv * inv + a.[1] * log t * inv + a.[2]
        + t * (a.[3] * 0.5 + t * (a.[4] / 3.0 + t * (a.[5] * 0.25 + t * a.[6] * 0.2)))
        + b0 * inv

    /// NASA-9 S/R.
    let inline nasa9SOverR (a: float[]) (b1: float) (t: float) =
        let inv = 1.0 / t
        -a.[0] * inv * inv * 0.5 - a.[1] * inv + a.[2] * log t
        + t * (a.[3] + t * (a.[4] * 0.5 + t * (a.[5] / 3.0 + t * a.[6] * 0.25)))
        + b1

    /// Outcome of a bracketed root search.
    type RootResult =
        { Root       : float
          Iterations : int
          Residual   : float
          Converged  : bool }

    /// Bisection on a bracketed root, with a real convergence test.
    ///
    /// Returns Failure when the bracket does not contain a sign change, rather
    /// than searching inside it and returning a midpoint that means nothing.
    /// That case is not hypothetical: it is what a physically impossible input
    /// produces, and silently returning the middle of the interval is how such
    /// an input reaches a report.
    let bisect (f: float -> float) (lo: float) (hi: float)
               (tolerance: float) (maxIterations: int) (name: string)
               : Returns<RootResult, ThermoMessage> =
        if hi <= lo then
            fail (InvalidMixture $"{name}: the bracket [{lo}, {hi}] is empty")
        else
            let fLo = f lo
            let fHi = f hi
            if Double.IsNaN fLo || Double.IsNaN fHi then
                fail (CorrelationExtrapolated
                        (name, "the function is undefined at a bracket endpoint"))
            elif fLo = 0.0 then
                ok { Root = lo; Iterations = 0; Residual = 0.0; Converged = true }
            elif fHi = 0.0 then
                ok { Root = hi; Iterations = 0; Residual = 0.0; Converged = true }
            elif (fLo > 0.0) = (fHi > 0.0) then
                fail (CorrelationExtrapolated
                        (name,
                         $"the root is not bracketed: f(%g{lo}) = %g{fLo} and f(%g{hi}) = %g{fHi} "
                         + "have the same sign. The input is outside the range where a solution "
                         + "exists."))
            else
                let mutable a = lo
                let mutable b = hi
                let mutable fa = fLo
                let mutable i = 0
                let mutable finished = false
                let mutable root = lo
                let mutable residual = fLo

                while not finished && i < maxIterations do
                    let mid = 0.5 * (a + b)
                    let fm = f mid
                    root <- mid
                    residual <- fm
                    if fm = 0.0 || (b - a) * 0.5 <= tolerance then finished <- true
                    else
                        if (fm > 0.0) = (fa > 0.0) then
                            a <- mid
                            fa <- fm
                        else b <- mid
                        i <- i + 1

                ok { Root = root; Iterations = i; Residual = residual; Converged = finished }
                |> warnIf (not finished)
                          (CorrelationExtrapolated
                            (name,
                             $"did not converge in {maxIterations} iterations; residual %g{residual}"))

    /// Bisection on a logarithmic scale, for quantities spanning many decades -
    /// partial pressures, equilibrium constants. Bisecting the linear variable
    /// there wastes most of its iterations in the wrong decade.
    let bisectLog (f: float -> float) (lo: float) (hi: float)
                  (relativeTolerance: float) (maxIterations: int) (name: string) =
        if lo <= 0.0 || hi <= 0.0 then
            fail (InvalidMixture $"{name}: logarithmic bisection needs positive bounds")
        else
            bisect (fun x -> f (exp x)) (log lo) (log hi)
                   relativeTolerance maxIterations name
            >>= fun r -> ok { r with Root = exp r.Root }
