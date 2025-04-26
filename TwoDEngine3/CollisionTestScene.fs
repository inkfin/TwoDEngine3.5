module CollisionTestScene

open System
open System.Numerics
open System.IO
open System.Drawing
open AngelCodeTextRenderer
open ImageExtensions
open InputManagerWinRawInput
open GraphicsManagerSFML
open TDE3ManagerInterfaces.InputDevices
open TDE3ManagerInterfaces.GraphicsManagerInterface
open TDE3ManagerInterfaces.TextRendererInterfaces
open TDE3ManagerInterfaces.CollisionManagerInterface
open PhysicsObjectFactory
open PhysicsObjectFactory.Factory
open PhysicsSolver.Solver
open Player

open TracyProfiler

/// Main entry point
let Start() =
    // Retrieve engine modules
    let graphics = ManagerUtils.TryGetManager<GraphicsManager> ()
    let textRenderer = ManagerUtils.TryGetManager<TextManager> ()
    let inputManager = ManagerUtils.TryGetManager<InputDeviceInterface> ()

    // Explicitly annotate Window type to avoid type inference errors
    let window: Window = graphics.OpenWindow (Windowed (800u, 600u)) "Collision Test Scene"

    // Load texture atlas resource
    let atlas = File.Open("Assets/ballCollisionTest2.png", FileMode.Open) |> window.LoadImage
    let ballImg = atlas.SubImage (Rectangle(Point(0, 0), Size(44, 44)))         // Ball sub-image
    let platformImg = Some (atlas.SubImage (Rectangle(Point(0, 480), Size(500, 20)))) // Platform image
    let font = textRenderer.LoadFont window "Assets/Basic.fnt"                 // Font resource

    // Initialize platform object using external module
    let platform = generatePlatform 250.0f 490.0f 500.0f 20.0f platformImg

    // Set ball generation parameters and call factory method
    let ballCount = 10
    let ballMinX, ballMaxX = 100, 400
    let ballMinY, ballMaxY = 50, 200
    let mutable balls = generateBalls ballCount ballImg ballMinX ballMaxX ballMinY ballMaxY

    let mutable lastTime = DateTime.Now

    /// Main logic loop: update scene and render
    let rec logic (window: Window) =
        if window.IsOpen() && not (Key.IsKeyDown Key.ESC) then
            let currentTime = DateTime.Now
            let deltaMS = (currentTime - lastTime).Milliseconds

            if deltaMS > 10 then
                lastTime <- currentTime
                let dt = float32 deltaMS

                do
                    use _ = Profiler.BeginEvent("Physics Update")
                    // Call physics solver each frame for collision simulation and rotation update
                    balls <- solve balls platform dt 5

                do
                    use _ = Profiler.BeginEvent("Render")

                    // Clear previous frame image
                    window.Clear(Color.Black)

                    // Draw platform
                    match platform.img with
                    | Some img ->
                        let offsetX = float32 -img.Size.X / 2.0f
                        let offsetY = float32 -img.Size.Y / 2.0f
                        let xform =
                            window.TranslationTransform platform.pos.X platform.pos.Y
                            |> fun t -> t.Multiply (window.TranslationTransform offsetX offsetY)
                        window.DrawImage xform img
                    | None -> ()

                    // Draw all balls (including rotation angle)
                    balls |> List.iter (fun ball ->
                        let offsetX = float32 -ball.img.Size.X / 2.0f
                        let offsetY = float32 -ball.img.Size.Y / 2.0f
                        let xform =
                            window.TranslationTransform ball.pos.X ball.pos.Y
                            |> fun t -> t.Multiply (window.RotationTransform ball.angle)
                            |> fun t -> t.Multiply (window.TranslationTransform offsetX offsetY)
                        window.DrawImage xform ball.img
                    )

                    // Display FPS information
                    let fpsText = sprintf "FPS: %d | Balls: %d" (1000 / deltaMS) balls.Length
                    font.MakeText fpsText
                    |> fun t -> t.Draw window window.IdentityTransform

                    window.Show()

            Profiler.ProfileFrame("main_loop")
            logic window

    // Start window logic loop
    window.Start(logic)
    Profiler.Dispose()
