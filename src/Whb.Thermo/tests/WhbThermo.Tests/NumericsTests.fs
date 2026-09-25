module WhbThermo.Tests.NumericsTests

open System
open Xunit
open Ganfoss.ROP
open WhbThermo.Domain

let private value r =
    match r with
    | Success (v, _) -> v
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

// ---------- integer powers ----------

/// The fast paths must be exactly equal to Math.Pow for the exponents they
/// replace, not merely close: a Horner rewrite that shifts the last bit would
/// move every regression anchor in the suite.
[<Fact>]
let ``integer powers agree with Math.Pow to full precision`` () =
    for x in [ 0.5; 1.0; 2.0; 7.3; 298.15; 1673.15; 6000.0 ] do
        Assert.Equal(Math.Pow(x, 2.0), Numerics.sq x, 12)
        Assert.Equal(Math.Pow(x, 3.0), Numerics.cube x, 12)
        for n in [ 0; 1; 2; 3; 4; 5; -1; -2 ] do
            Assert.Equal(Math.Pow(x, float n), Numerics.powi x n, 10)

/// Horner must reproduce the naive polynomial to within rounding.
[<Fact>]
let ``Horner reproduces the direct polynomial`` () =
    let a = [| 1.5; -2.3; 0.7; -0.04; 0.0012 |]
    for x in [ 0.1; 1.0; 10.0; 300.0; 1500.0 ] do
        let direct =
            a.[0] + a.[1] * x + a.[2] * x * x + a.[3] * Math.Pow(x, 3.0)
            + a.[4] * Math.Pow(x, 4.0)
        let horner = Numerics.horner a x
        Assert.True(abs (horner - direct) / max (abs direct) 1.0 < 1e-12,
                    $"at x = {x}: {horner} against {direct}")

/// The NASA-9 helpers must match the textbook form exactly. This is the check
/// that the Horner rewrite of the hottest path in the library is faithful.
[<Fact>]
let ``the NASA-9 helpers match the textbook form`` () =
    let a = [| 1.489e4; -292.2; 5.724; -8.176e-3; 1.457e-5; -1.088e-8; 3.028e-12 |]
    let b0, b1 = -1.303e4, -7.859

    for t in [ 300.0; 800.0; 1673.15; 5000.0 ] do
        let cpDirect =
            a.[0] / (t * t) + a.[1] / t + a.[2] + a.[3] * t
            + a.[4] * Math.Pow(t, 2.0) + a.[5] * Math.Pow(t, 3.0) + a.[6] * Math.Pow(t, 4.0)
        Assert.Equal(cpDirect, Numerics.nasa9CpOverR a t, 9)

        let hDirect =
            -a.[0] / (t * t) + a.[1] * log t / t + a.[2] + a.[3] * t / 2.0
            + a.[4] * Math.Pow(t, 2.0) / 3.0 + a.[5] * Math.Pow(t, 3.0) / 4.0
            + a.[6] * Math.Pow(t, 4.0) / 5.0 + b0 / t
        Assert.Equal(hDirect, Numerics.nasa9HOverRT a b0 t, 9)

        let sDirect =
            -a.[0] / (2.0 * t * t) - a.[1] / t + a.[2] * log t + a.[3] * t
            + a.[4] * Math.Pow(t, 2.0) / 2.0 + a.[5] * Math.Pow(t, 3.0) / 3.0
            + a.[6] * Math.Pow(t, 4.0) / 4.0 + b1
        Assert.Equal(sDirect, Numerics.nasa9SOverR a b1 t, 9)

// ---------- root finding ----------

[<Fact>]
let ``bisection converges on a simple root`` () =
    let r = Numerics.bisect (fun x -> x * x - 2.0) 0.0 2.0 1e-12 100 "sqrt 2" |> value
    Assert.True(r.Converged)
    Assert.Equal(sqrt 2.0, r.Root, 10)
    Assert.True(r.Iterations < 60, $"took {r.Iterations} iterations")

/// The fault this replaces: every bisection in the library ran a fixed count and
/// returned its midpoint regardless. An unbracketed root must FAIL, not produce
/// a number that means nothing.
[<Fact>]
let ``an unbracketed root is refused rather than guessed`` () =
    match Numerics.bisect (fun x -> x * x + 1.0) 0.0 2.0 1e-12 100 "no root" with
    | Failure msgs ->
        Assert.Contains(msgs, function CorrelationExtrapolated _ -> true | _ -> false)
    | Success (r, _) -> failwith $"returned {r.Root} for a function with no root"

[<Fact>]
let ``an empty bracket is refused`` () =
    match Numerics.bisect (fun x -> x) 2.0 1.0 1e-12 100 "reversed" with
    | Failure _ -> ()
    | Success _ -> failwith "a reversed bracket must be refused"

[<Fact>]
let ``a root exactly on a bracket endpoint is found without iterating`` () =
    let r = Numerics.bisect (fun x -> x - 1.0) 1.0 5.0 1e-12 100 "endpoint" |> value
    Assert.Equal(1.0, r.Root, 12)
    Assert.Equal(0, r.Iterations)

/// Stopping on tolerance rather than on a fixed count: a loose tolerance must
/// cost far fewer iterations than a tight one.
[<Fact>]
let ``iteration count follows the tolerance`` () =
    let loose = Numerics.bisect (fun x -> x * x - 2.0) 0.0 2.0 1e-3 200 "loose" |> value
    let tight = Numerics.bisect (fun x -> x * x - 2.0) 0.0 2.0 1e-14 200 "tight" |> value
    Assert.True(loose.Iterations < tight.Iterations,
                $"loose {loose.Iterations} against tight {tight.Iterations}")

/// Non-convergence must be reported, not hidden behind a plausible number.
[<Fact>]
let ``failure to converge is reported`` () =
    match Numerics.bisect (fun x -> x * x - 2.0) 0.0 2.0 1e-15 3 "starved" with
    | Success (r, warnings) ->
        Assert.False(r.Converged)
        Assert.NotEmpty(warnings)
    | Failure _ -> failwith "should succeed with a warning"

/// Logarithmic bisection for quantities spanning decades. Bisecting the linear
/// variable over thirty decades spends almost every iteration in the wrong one.
[<Fact>]
let ``logarithmic bisection handles a root spanning many decades`` () =
    let target = 3.7e-18
    let r =
        Numerics.bisectLog (fun x -> x - target) 1e-30 1e-5 1e-12 200 "tiny root" |> value
    Assert.True(r.Converged)
    Assert.True(abs (r.Root - target) / target < 1e-6, $"got {r.Root}")

[<Fact>]
let ``logarithmic bisection refuses non-positive bounds`` () =
    match Numerics.bisectLog (fun x -> x - 1.0) 0.0 10.0 1e-9 100 "bad bounds" with
    | Failure _ -> ()
    | Success _ -> failwith "log bisection needs positive bounds"

/// A function undefined at an endpoint must fail rather than propagating NaN
/// through the comparison, where it would silently take the wrong branch.
[<Fact>]
let ``an undefined endpoint is refused`` () =
    match Numerics.bisect (fun x -> sqrt (x - 5.0)) 0.0 10.0 1e-9 100 "nan endpoint" with
    | Failure _ -> ()
    | Success _ -> failwith "a NaN endpoint must be refused"
