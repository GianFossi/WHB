namespace WhbThermo.Domain

/// Units of Measure used across the WHB thermal engine.
/// All internal computation is SI; conversion happens only at the boundary.
[<AutoOpen>]
module Units =

    [<Measure>] type K
    [<Measure>] type degC
    [<Measure>] type Pa
    [<Measure>] type bar
    [<Measure>] type kg
    [<Measure>] type mol
    [<Measure>] type kmol
    [<Measure>] type m
    [<Measure>] type s
    [<Measure>] type J
    [<Measure>] type W

    /// Universal gas constant.
    let Ru = 8.31446261815324<J/(mol*K)>

    /// Absolute zero offset.
    let inline toKelvin (t: float<degC>) : float<K> = (float t + 273.15) * 1.0<K>
    let inline toCelsius (t: float<K>) : float<degC> = (float t - 273.15) * 1.0<degC>
    let inline barToPa (p: float<bar>) : float<Pa> = float p * 1e5<Pa>
