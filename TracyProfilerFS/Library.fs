namespace TracyProfilerFS

open System
open System.Collections.Generic
open System.Runtime.CompilerServices
open bottlenoselabs.C2CS.Runtime
open ColorType

/// IDisposable scope that ends a profiling zone when disposed
/// Implements disposal to signal Tracy's zone end
type ProfilerScope(ctx: Tracy.PInvoke.TracyCZoneCtx) =
    interface IDisposable with
        member _.Dispose() = Tracy.PInvoke.TracyEmitZoneEnd(ctx)

/// Static Profiler type exposing Tracy bindings for F#
type Profiler private() =
    // Cache of source file and function name CString pairs
    static let allocations = Dictionary<string, (CString * CString)>()

    // Cached CString for frame name in ProfileFrame
    static let mutable frameName: CString = Unchecked.defaultof<_>

    /// Begins a profiling zone.
    /// Caller information (member name, file path, line number) is captured automatically.
    /// Optionally specify a color for the zone.
    static member BeginEvent
        ([<CallerMemberName>] ?functionName: string,
         [<CallerFilePath>]   ?scriptPath:   string,
         [<CallerLineNumber>] ?lineNumber:   int,
         ?colorType: ColorType)
        : ProfilerScope =

        // Apply default values for optional parameters
        let fnName   = defaultArg functionName ""
        let filePath = defaultArg scriptPath ""
        let line     = defaultArg lineNumber 0
        let color    = defaultArg colorType ColorType.Default

        // Retrieve or allocate CString pair for source location
        let src, func =
            match allocations.TryGetValue fnName with
            | true, existing -> existing
            | _ ->
                let srcCString = CString filePath
                let fnCString  = CString fnName
                allocations.Add(fnName, (srcCString, fnCString))
                (srcCString, fnCString)

        // Allocate a source location and begin the zone
        let srcLoc = Tracy.PInvoke.TracyAllocSrcloc(uint32 line, src, uint64 filePath.Length, func, uint64 fnName.Length)
        let ctx    = Tracy.PInvoke.TracyEmitZoneBeginAlloc(srcLoc, 1)

        // Apply custom color if specified
        if color <> ColorType.Default then
            Tracy.PInvoke.TracyEmitZoneColor(ctx, uint32 color)

        // Return a disposable scope that will end the zone on Dispose()
        new ProfilerScope(ctx)

    /// Marks the end of the current frame for Tracy visualization
    static member ProfileFrame(name: string) =
        // Cache the frameName CString on first invocation
        if frameName = Unchecked.defaultof<_> then
            frameName <- CString name
        Tracy.PInvoke.TracyEmitFrameMark(frameName)

    /// Releases all allocated CStrings; call this on application shutdown
    static member Dispose() =
        for srcCString, fnCString in allocations.Values do
            srcCString.Dispose()
            fnCString.Dispose()
