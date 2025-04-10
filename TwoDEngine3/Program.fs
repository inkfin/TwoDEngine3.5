module TwoDEngine3
open System
open System.IO
open System.Reflection
open ManagerRegistry

type SysColor = System.Drawing.Color

(*   Asteriods test program by JPK *)


let rec RecursiveGetAssemblies (path:string option) =
    let mypath =
                 match path with
                 | Some s -> s
                 | None -> Environment.CurrentDirectory
    let dinfo = DirectoryInfo(mypath)
    let assemblies =
        dinfo.EnumerateFiles("*.dll")
        |> Seq.map (fun fileInfo -> Assembly.LoadFile fileInfo.FullName)
    dinfo.GetDirectories()
    |> Seq.fold (
            fun state dinfo ->
                Seq.concat [RecursiveGetAssemblies (Some dinfo.FullName);assemblies] 
        ) assemblies
    
                 



[<EntryPoint>]
[<STAThread>]
let main argv =
    //tempoary: set up the managers
    // To be replaced with runtime dynmaic loading
    
    RecursiveGetAssemblies None
    |> Seq.iter (fun assembly ->
        assembly.GetTypes()
        |> Array.filter (fun t ->
                CustomAttributeExtensions.IsDefined(t,typeof<Manager>)
            )
        |> Array.iter (fun t -> ManagerRegistry.addManager(t))
     )
    
    // Asteroids.Start()
    CollisionTestScene.Start()
    0 // return an integer exit code
