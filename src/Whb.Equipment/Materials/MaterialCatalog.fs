namespace Whb.Equipment

open System

module Materials =

    /// <summary>
    /// Static material data used by equipment and BOM models.
    /// </summary>
    [<CLIMutable>]
    type MaterialProperties =
        { Id: string
          Name: string
          Density: float
          Conductivity20C: float
          YoungModulus20C: float
          Yield20C: float
          Notes: string }

    /// <summary>
    /// Fluid density used to derive held-up internal-fluid weight from component geometry.
    /// </summary>
    [<CLIMutable>]
    type FluidProperties =
        { Name: string
          Density: float }

    /// <summary>
    /// Abstraction for material lookup so `Whb.Core` can state what physical objects need
    /// without embedding a storage choice or database path into the calculation assembly.
    /// </summary>
    type IMaterialPropertySource =
      /// <summary>Returns all materials available from the source.</summary>
        abstract member AllMaterials: unit -> MaterialProperties list
      /// <summary>Attempts to find a material by identifier or name.</summary>
        abstract member TryGetMaterial: string -> MaterialProperties option
      /// <summary>Returns a material by identifier or name, using the source default when absent.</summary>
        abstract member GetMaterial: string -> MaterialProperties

    let builtInMaterials =
        [ { Id = "SA-192"
            Name = "SA-192 / SA-210 A1"
            Density = 7850.0
            Conductivity20C = 52.0
            YoungModulus20C = 207e9
            Yield20C = 255e6
            Notes = "Tubi caldaia al carbonio." }
          { Id = "SA-213-T11"
            Name = "SA-213 T11"
            Density = 7850.0
            Conductivity20C = 42.0
            YoungModulus20C = 210e9
            Yield20C = 275e6
            Notes = "1.25Cr-0.5Mo per zone calde." }
          { Id = "SA-533B2"
            Name = "SA-533 Gr.B Cl.2"
            Density = 7850.0
            Conductivity20C = 41.0
            YoungModulus20C = 207e9
            Yield20C = 485e6
            Notes = "Lamiera recipiente a pressione." }
          { Id = "ALLOY-602CA"
            Name = "Alloy 602 CA"
            Density = 7600.0
            Conductivity20C = 10.5
            YoungModulus20C = 217e9
            Yield20C = 270e6
            Notes = "Liner ad alta temperatura." } ]

    let private matchesKey (key: string) (material: MaterialProperties) =
        material.Id.Equals(key, StringComparison.OrdinalIgnoreCase)
        || material.Name.Contains(key, StringComparison.OrdinalIgnoreCase)

    /// <summary>Attempts to find a built-in material by identifier or name.</summary>
    let tryGetMaterialByName (key: string) =
        builtInMaterials |> List.tryFind (matchesKey key)

    /// <summary>Returns a built-in material by identifier or name.</summary>
    let getMaterialByName (key: string) =
        tryGetMaterialByName key |> Option.defaultValue builtInMaterials.Head

    /// <summary>Provides material lookup against the built-in material catalogue.</summary>
    type BuiltInMaterialPropertySource() =
        interface IMaterialPropertySource with
            member _.AllMaterials() = builtInMaterials
            member _.TryGetMaterial(key: string) = tryGetMaterialByName key
            member _.GetMaterial(key: string) = getMaterialByName key
