namespace Whb.Core

/// <summary>
/// Provides constants functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Constants =

    /// <summary>
    /// Calculates or returns r for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let R = 8.31446261815324

    /// <summary>
    /// Calculates or returns sigmasb for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let sigmaSB = 5.670374419e-8

    /// <summary>
    /// Calculates or returns g for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let g = 9.80665

    /// <summary>
    /// Calculates or returns tc_water for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let Tc_water = 647.096

    /// <summary>
    /// Calculates or returns pc_water for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let Pc_water = 22.064e6

    /// <summary>
    /// Calculates or returns rhoc_water for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let Rhoc_water = 322.0

    /// <summary>
    /// Calculates or returns rw for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let Rw = 0.461526

    /// <summary>
    /// Calculates or returns ctok for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let inline cToK (t: float) = t + 273.15

    /// <summary>
    /// Calculates or returns ktoc for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let inline kToC (t: float) = t - 273.15

    /// <summary>
    /// Calculates or returns bartopa for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let inline barToPa (p: float) = p * 1.0e5

    /// <summary>
    /// Calculates or returns patobar for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let inline paToBar (p: float) = p / 1.0e5

    /// <summary>
    /// Calculates or returns mmtom for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let inline mmToM (x: float) = x / 1000.0

    /// <summary>
    /// Calculates or returns lmtd for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let lmtd (a: float) (b: float) =
        if a <= 0.0 || b <= 0.0 then 0.0
        elif abs (a - b) < 1e-9 then a
        else (a - b) / log (a / b)

    /// <summary>
    /// Calculates or returns bisect for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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

    /// <summary>
    /// Calculates or returns gradedaxialgrid for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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

    /// <summary>
    /// Calculates or returns fixedpoint for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
