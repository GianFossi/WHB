namespace Whb.Core

/// Costanti fisiche e conversioni di unità.
module Constants =

    /// Costante universale dei gas [J/(mol·K)]
    let R = 8.31446261815324

    /// Costante di Stefan-Boltzmann [W/(m²·K⁴)]
    let sigmaSB = 5.670374419e-8

    /// Accelerazione di gravità [m/s²]
    let g = 9.80665

    /// Temperatura critica dell'acqua [K]
    let Tc_water = 647.096

    /// Pressione critica dell'acqua [Pa]
    let Pc_water = 22.064e6

    /// Densità critica dell'acqua [kg/m³]
    let Rhoc_water = 322.0

    /// Costante specifica del vapor d'acqua [kJ/(kg·K)] (IAPWS-IF97)
    let Rw = 0.461526

    /// Converte una temperatura da gradi Celsius a kelvin.
    let inline cToK (t: float) = t + 273.15

    /// Converte una temperatura da kelvin a gradi Celsius.
    let inline kToC (t: float) = t - 273.15

    /// Converte una pressione da bar a pascal.
    let inline barToPa (p: float) = p * 1.0e5

    /// Converte una pressione da pascal a bar.
    let inline paToBar (p: float) = p / 1.0e5

    /// Converte una lunghezza da millimetri a metri.
    let inline mmToM (x: float) = x / 1000.0

    /// Media logaritmica di due valori positivi (con fallback su media aritmetica).
    let lmtd (a: float) (b: float) =
        if a <= 0.0 || b <= 0.0 then 0.0
        elif abs (a - b) < 1e-9 then a
        else (a - b) / log (a / b)

    /// Ricerca di radice con bisezione robusta.
    let bisect (f: float -> float) (lo: float) (hi: float) (tol: float) (maxIter: int) =
        let mutable a = min lo hi
        let mutable b = max lo hi
        let mutable fa = f a
        let mutable fb = f b
        if fa * fb > 0.0 then
            // nessun cambio di segno: restituisce l'estremo con residuo minore
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

    /// Maglia assiale **graduata**: celle fini all'imbocco (dove il gradiente
    /// termico e' ripido e dove finiscono le ferrule) e via via piu' grosse.
    ///   l      : lunghezza [m]
    ///   n      : numero di celle
    ///   refine : rapporto fra la cella uniforme e la prima cella (1 = uniforme)
    /// Restituisce (centri, ampiezze).
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

    /// Iterazione di punto fisso con sotto-rilassamento.
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
