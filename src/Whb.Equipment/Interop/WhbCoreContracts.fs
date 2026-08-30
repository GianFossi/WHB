namespace Whb.Equipment

module Interop =

    /// <summary>
    /// Port from `Whb.Core` into the equipment-model project. It states the assembled physical
    /// objects that the calculation core can expose without forcing this project to reference
    /// solver-side implementation details.
    /// </summary>
    type IWhbCoreEquipmentSnapshot =
        abstract member PackageName: string
        abstract member Whbs: WhbEquipment list
        abstract member Risers: PipelineEquipment list
        abstract member Downcomers: PipelineEquipment list
        abstract member SteamDrum: SteamDrumEquipment
        abstract member Notes: string
