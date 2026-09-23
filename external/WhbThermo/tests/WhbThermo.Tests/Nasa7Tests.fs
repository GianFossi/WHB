module WhbThermo.Tests.Nasa7Tests

open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data
open WhbThermo.Properties

let private R = 8.31446261815324

/// Build a species backed by a synthetic NASA-7 fit, so these tests exercise the
/// evaluator itself and stay independent of whichever Burcat file has been merged.
let private synthetic (molarMass: float) (low: float[]) (high: float[]) =
    let fit =
        { Coeff0 = 1.0e-5; SutherlandK = 100.0<K>; TRef = 273.15<K>
          TMin = 250.0<K>; TMax = 1800.0<K>; Source = "synthetic" }
    { Key = "SYN"
      Name = "Synthetic"
      MolarMass = molarMass * 1.0<kg/kmol>
      Viscosity = fit
      Conductivity = fit
      Cp = Nasa7 [ { TMin = 200.0<K>; TMax = 1000.0<K>; A = low; Source = "synthetic" }
                   { TMin = 1000.0<K>; TMax = 6000.0<K>; A = high; Source = "synthetic" } ]
      Critical = None }

let private value r =
    match r with
    | Success (v, _) -> v
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

/// Constant Cp/R = 4 on a species of M = 40 kg/kmol
/// => Cp = 4 * 8.3145 / 0.040 = 831.4 J/(kg*K).
[<Fact>]
let ``constant NASA-7 polynomial gives the analytic Cp`` () =
    let a = [| 4.0; 0.0; 0.0; 0.0; 0.0; -1.0e4; 5.0 |]
    let sp = synthetic 40.0 a a
    let cp = PureComponent.specificHeatMass sp 600.0<K> |> value
    Assert.Equal(4.0 * R / 0.040, float cp, 6)

/// The evaluator must switch blocks at T_common, not silently use the low block.
[<Fact>]
let ``segment selection switches at the common temperature`` () =
    let low  = [| 4.0; 0.0; 0.0; 0.0; 0.0; -1.0e4; 5.0 |]
    let high = [| 6.0; 0.0; 0.0; 0.0; 0.0; -1.0e4; 5.0 |]
    let sp = synthetic 40.0 low high
    let cpLow = PureComponent.specificHeatMass sp 500.0<K> |> value
    let cpHigh = PureComponent.specificHeatMass sp 1500.0<K> |> value
    Assert.Equal(4.0 * R / 0.040, float cpLow, 6)
    Assert.Equal(6.0 * R / 0.040, float cpHigh, 6)

/// For constant Cp, H(T) - H(298.15) = Cp_molar * (T - 298.15).
[<Fact>]
let ``NASA-7 enthalpy integral matches the analytic result`` () =
    let a = [| 4.0; 0.0; 0.0; 0.0; 0.0; -1.0e4; 5.0 |]
    let sp = synthetic 40.0 a a
    let h = PureComponent.enthalpy sp 800.0<K> |> value       // kJ/mol
    let expected = 4.0 * R * (800.0 - 298.15) / 1000.0
    Assert.Equal(expected, h, 6)

/// Unlike an anchor-only species, a NASA-7 species CAN produce an enthalpy.
[<Fact>]
let ``NASA-7 species supports enthalpy`` () =
    let a = [| 3.5; 1.0e-4; 0.0; 0.0; 0.0; -1.0e4; 5.0 |]
    let sp = synthetic 30.0 a a
    match PureComponent.enthalpy sp 900.0<K> with
    | Success _ -> ()
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

/// Extrapolation beyond the outer bounds still warns.
[<Fact>]
let ``NASA-7 outside range warns`` () =
    let a = [| 4.0; 0.0; 0.0; 0.0; 0.0; -1.0e4; 5.0 |]
    let sp = synthetic 40.0 a a
    match PureComponent.specificHeatMass sp 8000.0<K> with
    | Success (_, warnings) ->
        Assert.Contains(warnings, function OutsideFitRange _ -> true | _ -> false)
    | Failure _ -> failwith "expected success with warning"

/// A record with the wrong number of coefficients must be rejected at load time.
[<Fact>]
let ``malformed NASA-7 block fails to parse`` () =
    let json = """
    { "schemaVersion": "1.1", "description": "test", "species": [
      { "key": "BAD", "name": "Bad", "molarMass_kg_kmol": 40.0,
        "viscosity":    { "coeff0": 1e-5, "sutherlandK": 100, "tRefK": 273.15, "tMinK": 250, "tMaxK": 1800, "source": "t" },
        "conductivity": { "coeff0": 2e-2, "sutherlandK": 100, "tRefK": 273.15, "tMinK": 250, "tMaxK": 1800, "source": "t" },
        "cpModel": { "kind": "nasa7", "segments": [],
                     "nasa7Segments": [ { "tMinK": 200, "tMaxK": 1000, "a": [1,2,3], "source": "t" } ],
                     "anchorCp500C": 1.0 },
        "critical": null } ] }
    """
    match SpeciesDatabase.parse json with
    | Failure [ DatabaseParseError d ] -> Assert.Contains("expected 7", d)
    | _ -> failwith "expected a parse failure"
