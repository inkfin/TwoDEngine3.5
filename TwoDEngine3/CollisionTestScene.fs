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
open PhysicsSolver
open Player
open TracyProfiler

let Start() =
    let graphics = ManagerUtils.TryGetManager<GraphicsManager> ()
    let textRenderer = ManagerUtils.TryGetManager<TextManager> ()
    let inputManager = ManagerUtils.TryGetManager<InputDeviceInterface> ()
    let window: Window = graphics.OpenWindow (Windowed (800u, 600u)) "Collision Test Scene"

    let atlas = File.Open("Assets/ballCollisionTest4.png", FileMode.Open) |> window.LoadImage
    let ballImg = atlas.SubImage (Rectangle(Point(66, 17), Size(10, 10)))
    let platformImg = Some (atlas.SubImage (Rectangle(Point(0, 480), Size(800, 20))))
    let font = textRenderer.LoadFont window "Assets/Basic.fnt"

    let platform = generatePlatform 250.0f 490.0f 800.0f 20.0f platformImg

    let ballCount = 300
    let ballMinX, ballMaxX = 0, 400
    let ballMinY, ballMaxY = 50, 400
    let mutable balls = generateBalls ballCount ballImg ballMinX ballMaxX ballMinY ballMaxY

    // Create solver plugin instance
    let solver: ISolver = new FixedStepSolver(fixedDt = 0.005f, maxSteps = 1000, iterations = 5) :> ISolver

    let mutable lastTime = DateTime.Now

    let rec logic (window: Window) =
        if window.IsOpen() && not (Key.IsKeyDown Key.ESC) then
            let currentTime = DateTime.Now
            let delta = currentTime - lastTime

            if delta.TotalMilliseconds > 10.0 then
                lastTime <- currentTime
                let dt = float32 delta.TotalSeconds
                do
                    use _ = Profiler.BeginEvent("Physics Update")
                    balls <- solver.Step balls platform dt

                do
                    use _ = Profiler.BeginEvent("Render")
                    window.Clear(Color.Black)

                    match platform.img with
                    | Some img ->
                        let offsetX = float32 -img.Size.X / 2.0f
                        let offsetY = float32 -img.Size.Y / 2.0f
                        let xform =
                            window.TranslationTransform platform.pos.X platform.pos.Y
                            |> fun t -> t.Multiply (window.TranslationTransform offsetX offsetY)
                        window.DrawImage xform img
                    | None -> ()

                    balls |> List.iter (fun ball ->
                        let offsetX = float32 -ball.img.Size.X / 2.0f
                        let offsetY = float32 -ball.img.Size.Y / 2.0f
                        let xform =
                            window.TranslationTransform ball.pos.X ball.pos.Y
                            |> fun t -> t.Multiply (window.RotationTransform ball.angle)
                            |> fun t -> t.Multiply (window.TranslationTransform offsetX offsetY)
                        window.DrawImage xform ball.img
                    )

                    let fpsText = sprintf "FPS: %d | Balls: %d" (int (1000.0 / delta.TotalMilliseconds)) balls.Length
                    font.MakeText fpsText
                    |> fun t -> t.Draw window window.IdentityTransform

                    window.Show()

            Profiler.ProfileFrame("main_loop")
            logic window

    window.Start(logic)
    Profiler.Dispose()
