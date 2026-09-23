namespace Ganfoss

open ROP

[<AutoOpen>]
module ROP =

    type Returns<'TSuccess, 'TMessage> = ROP.Returns<'TSuccess, 'TMessage>

    let ok = Returns.ok
    let fail = Returns.fail
    let warn message source = Returns.bind (fun value -> Returns.warn message value) source
    let warnIf condition message source = Returns.warnIf (fun _ -> condition) message source
    let traverseList = Returns.traverseList
    let sequenceList = Returns.sequenceList
    let returns = ROP.ReturnsBuilder()

    let inline (>>=) source binder = Returns.bind binder source

    let (|Success|Failure|) (source: Returns<'TSuccess, 'TMessage>) =
        match source with
        | Returns.Success (value, warnings) -> Success (value, warnings)
        | Returns.Failure errors -> Failure errors
