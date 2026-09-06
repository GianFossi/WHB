namespace Whb.Equipment

/// <summary>
/// Physical package snapshot of the WHB, steam drum and connecting pipelines.
/// </summary>
[<CLIMutable>]
type EquipmentPackage =
    { Name: string
      Whbs: WhbEquipment list
      Risers: PipelineEquipment list
      Downcomers: PipelineEquipment list
      SteamDrum: SteamDrumEquipment
      Notes: string }
    /// <summary>Returns aggregate geometry and material metrics for the package.</summary>
    member x.Metrics =
        Metrics.combine
            [ yield! x.Whbs |> Seq.map (fun whb -> whb.Metrics)
              yield! x.Risers |> Seq.map (fun pipeline -> pipeline.Metrics)
              yield! x.Downcomers |> Seq.map (fun pipeline -> pipeline.Metrics)
              yield x.SteamDrum.Metrics ]

[<RequireQualifiedAccess>]
module EquipmentPackage =

  /// <summary>Creates an equipment package from a core snapshot contract.</summary>
    let ofWhbCore (source: Interop.IWhbCoreEquipmentSnapshot) =
        { Name = source.PackageName
          Whbs = source.Whbs
          Risers = source.Risers
          Downcomers = source.Downcomers
          SteamDrum = source.SteamDrum
          Notes = source.Notes }
