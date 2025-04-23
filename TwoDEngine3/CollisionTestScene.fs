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

/// 主函数入口
let Start() =
    // 获取各个引擎模块
    let graphics = ManagerUtils.TryGetManager<GraphicsManager> ()
    let textRenderer = ManagerUtils.TryGetManager<TextManager> ()
    let inputManager = ManagerUtils.TryGetManager<InputDeviceInterface> ()

    // 显式注解 Window 类型，避免类型推断错误
    let window: Window = graphics.OpenWindow (Windowed (800u, 600u)) "Collision Test Scene"

    // 加载纹理图集资源
    let atlas = File.Open("Assets/ballCollisionTest2.png", FileMode.Open) |> window.LoadImage
    let ballImg = atlas.SubImage (Rectangle(Point(0, 0), Size(44, 44)))         // 小球子图
    let platformImg = Some (atlas.SubImage (Rectangle(Point(0, 480), Size(500, 20)))) // 平台图
    let font = textRenderer.LoadFont window "Assets/Basic.fnt"                 // 字体资源

    // 初始化平台对象，调用外部模块生成
    let platform = generatePlatform 250.0f 490.0f 500.0f 20.0f platformImg

    // 设置小球生成参数并调用工厂方法
    let ballCount = 10
    let ballMinX, ballMaxX = 100, 400
    let ballMinY, ballMaxY = 50, 200
    let mutable balls = generateBalls ballCount ballImg ballMinX ballMaxX ballMinY ballMaxY

    let mutable lastTime = DateTime.Now

    /// 主逻辑循环，更新场景并绘制
    let rec logic (window: Window) =
        if window.IsOpen() && not (Key.IsKeyDown Key.ESC) then
            let currentTime = DateTime.Now
            let deltaMS = (currentTime - lastTime).Milliseconds

            if deltaMS > 10 then
                lastTime <- currentTime
                let dt = float32 deltaMS

                // 每帧调用物理求解器进行碰撞模拟和旋转更新
                balls <- solve balls platform dt 5

                // 清除上一帧图像
                window.Clear(Color.Black)

                // 绘制平台
                match platform.img with
                | Some img ->
                    let offsetX = float32 -img.Size.X / 2.0f
                    let offsetY = float32 -img.Size.Y / 2.0f
                    let xform =
                        window.TranslationTransform platform.pos.X platform.pos.Y
                        |> fun t -> t.Multiply (window.TranslationTransform offsetX offsetY)
                    window.DrawImage xform img
                | None -> ()

                // 绘制所有小球（包含旋转角度）
                balls |> List.iter (fun ball ->
                    let offsetX = float32 -ball.img.Size.X / 2.0f
                    let offsetY = float32 -ball.img.Size.Y / 2.0f
                    let xform =
                        window.TranslationTransform ball.pos.X ball.pos.Y
                        |> fun t -> t.Multiply (window.RotationTransform ball.angle)
                        |> fun t -> t.Multiply (window.TranslationTransform offsetX offsetY)
                    window.DrawImage xform ball.img
                )

                // 显示帧率信息
                let fpsText = sprintf "FPS: %d | Balls: %d" (1000 / deltaMS) balls.Length
                font.MakeText fpsText
                |> fun t -> t.Draw window window.IdentityTransform

                window.Show()

            logic window

    // 启动窗口逻辑循环
    window.Start(logic)