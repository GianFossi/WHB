namespace Whb.Core
module Constants =
    let R = 8.31446261815324
    let sigmaSB = 5.670374419e-8
    let g = 9.80665
    let Tc_water = 647.096
    let Pc_water = 22.064e6
    let Rhoc_water = 322.0
    let Rw = 0.461526
    let inline cToK (t: float) = t + 273.15
    let inline kToC (t: float) = t - 273.15
    let inline barToPa (p: float) = p * 1.0e5
    let inline paToBar (p: float) = p / 1.0e5
    let inline mmToM (x: float) = x / 1000.0
    let lmtd (a: float) (b: float) =
        if a <= 0.0 || b <= 0.0 then 0.0
        elif abs (a - b) < 1e-9 then a
        else (a - b) / log (a / b)
    let bisect (f: float -> float) (lo: float) (hi: float) (tol: float) (maxIter: int) =
        let mutable a = min lo hi
        let mutable b = max lo hi
        let mutable fa = f a
        let mutable fb = f b
        if fa * fb > 0.0 then
            if abs fa < abs fb then a else b
        else
            let mutable i = 0
            let mutable m = 0.5 * (a + b)
            while i < maxIter && (b - a) > tol do
                m <- 0.5 * (a + b)
                let fm = f m
                if fa * fm <= 0.0 then
                    b <- m
                    fb <- fm
                else
                    a <- m
                    fa <- fm
                i <- i + 1
            0.5 * (a + b)
    /// What a bracketed solve actually did. `bisect` collapses all three into a bare number,
    /// which makes a clamped endpoint indistinguishable from a converged root.
    type RootStatus =
        /// A sign change was bracketed and the interval was reduced to the tolerance.
        | RootFound
        /// No sign change in the bracket: the returned value is the endpoint with the smaller
        /// residual, not a root.
        | NoSignChange
        /// A sign change existed but the iteration cap was reached first.
        | IterationCap
    /// Bisection that reports what it did. Returns exactly the same value as `bisect` for the
    /// same arguments, so it can be substituted anywhere without moving a number.
    let bisectWithStatus (f: float -> float) (lo: float) (hi: float) (tol: float) (maxIter: int) =
        let mutable a = min lo hi
        let mutable b = max lo hi
        let mutable fa = f a
        let mutable fb = f b
        if fa * fb > 0.0 then
            ((if abs fa < abs fb then a else b), NoSignChange)
        else
            let mutable i = 0
            let mutable m = 0.5 * (a + b)
            while i < maxIter && (b - a) > tol do
                m <- 0.5 * (a + b)
                let fm = f m
                if fa * fm <= 0.0 then
                    b <- m
                    fb <- fm
                else
                    a <- m
                    fa <- fm
                i <- i + 1
            (0.5 * (a + b), (if (b - a) > tol then IterationCap else RootFound))
    /// Counts sign changes of <paramref name="f"/> on a coarse scan of the bracket. More than
    /// one means the equation has multiple solutions there, and any single-root solver is
    /// picking one of them by the path it happens to take rather than by the physics.
    let countSignChanges (f: float -> float) (lo: float) (hi: float) (samples: int) =
        let n = max 2 samples
        let mutable changes = 0
        // Track the last non-zero sign rather than the last value: a sample landing exactly on
        // a root would otherwise mask the crossing on one side and double-count it on the other.
        let mutable lastSign = 0
        for k in 0 .. n do
            let x = lo + (hi - lo) * float k / float n
            let s = sign (f x)
            if s <> 0 then
                if lastSign <> 0 && s <> lastSign then changes <- changes + 1
                lastSign <- s
        changes
    /// Bracketed root finder (Brent 1973: bisection + secant + inverse quadratic
    /// interpolation). Same contract as `bisect` - same bracket, same fallback when
    /// the interval does not contain a sign change - but it converges superlinearly,
    /// so it reaches a much tighter tolerance in far fewer function evaluations.
    let brent (f: float -> float) (lo: float) (hi: float) (tol: float) (maxIter: int) =
        let mutable a = min lo hi
        let mutable b = max lo hi
        let mutable fa = f a
        let mutable fb = f b
        if fa * fb > 0.0 then
            if abs fa < abs fb then a else b
        else
            let eps = 2.22e-16
            let mutable c = a
            let mutable fc = fa
            let mutable d = b - a
            let mutable e = d
            let mutable i = 0
            let mutable stop = false
            while i < maxIter && not stop do
                if (fb > 0.0 && fc > 0.0) || (fb < 0.0 && fc < 0.0) then
                    c <- a
                    fc <- fa
                    d <- b - a
                    e <- d
                if abs fc < abs fb then
                    a <- b
                    b <- c
                    c <- a
                    fa <- fb
                    fb <- fc
                    fc <- fa
                let tol1 = 2.0 * eps * abs b + 0.5 * tol
                let xm = 0.5 * (c - b)
                if abs xm <= tol1 || fb = 0.0 then stop <- true
                else
                    if abs e >= tol1 && abs fa > abs fb then
                        let s = fb / fa
                        let mutable p = 0.0
                        let mutable q = 0.0
                        if a = c then
                            p <- 2.0 * xm * s
                            q <- 1.0 - s
                        else
                            let qq = fa / fc
                            let r = fb / fc
                            p <- s * (2.0 * xm * qq * (qq - r) - (b - a) * (r - 1.0))
                            q <- (qq - 1.0) * (r - 1.0) * (s - 1.0)
                        if p > 0.0 then q <- -q
                        let pAbs = abs p
                        let lim1 = 3.0 * xm * q - abs (tol1 * q)
                        let lim2 = abs (e * q)
                        if 2.0 * pAbs < min lim1 lim2 then
                            e <- d
                            d <- p / q
                        else
                            d <- xm
                            e <- d
                    else
                        d <- xm
                        e <- d
                    a <- b
                    fa <- fb
                    b <- b + (if abs d > tol1 then d elif xm >= 0.0 then tol1 else -tol1)
                    fb <- f b
                    i <- i + 1
            b
    /// Newton-Raphson for a strictly increasing residual, safeguarded by a bracket
    /// assumed to contain the root: any step that would leave the current bracket, or
    /// that has no usable derivative, is replaced by a bisection step. The bracket is
    /// tightened from the sign of every residual, so the iteration cannot diverge and
    /// converges quadratically instead of one bit per evaluation.
    /// <paramref name="fdf"/> returns the residual and its derivative in one pass.
    let newtonIncreasing (fdf: float -> struct (float * float)) (lo: float) (hi: float)
                         (x0: float) (tol: float) (maxIter: int) =
        let mutable a = min lo hi
        let mutable b = max lo hi
        let mutable x = max a (min b x0)
        let mutable i = 0
        let mutable stop = false
        while i < maxIter && not stop do
            let struct (f, df) = fdf x
            if f > 0.0 then b <- x else a <- x
            let xNext =
                if df > 0.0 then
                    let cand = x - f / df
                    if cand > a && cand < b then cand else 0.5 * (a + b)
                else 0.5 * (a + b)
            stop <- abs (xNext - x) <= tol || f = 0.0
            x <- xNext
            i <- i + 1
        x
    let gradedAxialGrid (l: float) (n: int) (refine: float) =
        let n = max 4 n
        if refine <= 1.0001 then
            let dz = l / float n
            (Array.init n (fun i -> (float i + 0.5) * dz), Array.create n dz)
        else
            let dz0 = l / float n / refine
            let target = l / dz0
            let f (r: float) =
                if abs (r - 1.0) < 1e-9 then float n - target
                else (r ** float n - 1.0) / (r - 1.0) - target
            let r = bisect f 1.0000001 1.5 1e-12 200
            let dz = Array.init n (fun i -> dz0 * r ** float i)
            let s = Array.sum dz
            let dz = dz |> Array.map (fun d -> d * l / s)
            let zc = Array.zeroCreate n
            let mutable acc = 0.0
            for i in 0 .. n - 1 do
                zc.[i] <- acc + dz.[i] / 2.0
                acc <- acc + dz.[i]
            (zc, dz)
    let fixedPoint (f: float -> float) (x0: float) (relax: float) (tol: float) (maxIter: int) =
        let mutable x = x0
        let mutable i = 0
        let mutable err = 1.0
        while i < maxIter && err > tol do
            let xn = f x
            let xr = x + relax * (xn - x)
            err <- abs (xr - x) / (abs xr + 1e-12)
            x <- xr
            i <- i + 1
        x


