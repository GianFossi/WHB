namespace Whb.Equipment

/// <summary>
/// Normal operating water levels inside the steam drum [m], relative to the chosen local datum.
/// </summary>
[<CLIMutable>]
type LevelDefinition =
    { LowLow: float
      Low: float
      Normal: float
      High: float
      HighHigh: float }

/// <summary>
/// Physical WHB equipment assembly, made of explicit components and separated from the
/// solver-side thermal and hydraulic modules.
/// </summary>
[<CLIMutable>]
type WhbEquipment =
    { Id: string
      Name: string
      Bom: Bom.BomItem
      Components: Component list }
    /// <summary>Returns aggregate metrics for all WHB components.</summary>
    member x.Metrics = x.Components |> Component.totalMetrics

/// <summary>
/// Physical steam-drum assembly, independent of the hydraulic and separator calculation modules.
/// </summary>
[<CLIMutable>]
type SteamDrumEquipment =
    { Id: string
      Name: string
      Bom: Bom.BomItem
      Components: Component list
      Levels: LevelDefinition }
    /// <summary>Returns aggregate metrics for all steam-drum components.</summary>
    member x.Metrics = x.Components |> Component.totalMetrics

[<RequireQualifiedAccess>]
module EquipmentAssemblies =

    /// <summary>Creates a tube-bundle assembly from its tube bank and accessory components.</summary>
    let tubeBundle id name bom tubeBank otherComponents =
        Component.createAssembly id name bom (tubeBank :: otherComponents)

    /// <summary>Creates a central-bypass assembly.</summary>
    let centralBypass id name bom components =
        Component.createAssembly id name bom components

    /// <summary>Creates a steam-drum component assembly.</summary>
    let steamDrumSection id name bom components =
        Component.createAssembly id name bom components
