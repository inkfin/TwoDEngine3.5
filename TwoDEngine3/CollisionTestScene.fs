module CollisionTestScene

open System
open System.Numerics
open System.IO
open System.Drawing
open AngelCodeTextRenderer
open ImageExtensions
open InputManagerWinRawInput
open GraphicsManagerSFML
open TDE3ManagerInterfaces.CollisionManagerInterface
open TDE3ManagerInterfaces.InputDevices
open TDE3ManagerInterfaces.GraphicsManagerInterface
open TDE3ManagerInterfaces.TextRendererInterfaces
open Player

// 用于产生随机数和生成随机浮点数
let globalRandom = System.Random()
let RandomFloat (low: float) (high: float) =
    globalRandom.NextDouble() * (high - low) + low

/// 定义小球类型（含旋转信息）
type Ball = {
    pos: Vector2
    vel: Vector2
    radius: float32
    img: Image
    angle: float32        // 当前旋转角度（度）
    angularVel: float32   // 当前角速度（度/ms）
}

/// 平台类型
type Platform = {
    pos: Vector2
    width: float32
    height: float32
    img: Image option
}

/// 物理参数
let gravity = 0.001f
let restitution = 0.8f

// clamp 辅助函数
let clamp (value: float32) (minVal: float32) (maxVal: float32) =
    if value < minVal then minVal
    elif value > maxVal then maxVal
    else value

/// Ball 状态结构体（用于 PBD）
type BallState = {
    ball: Ball
    oldPos: Vector2
    mutable p: Vector2
}

/// PBD 主求解函数
let pbdSolveBalls (balls: Ball list) (platform: Platform) (dt: float32) (iterations: int) : Ball list =
    let stateArray =
        balls
        |> List.map (fun ball -> { ball = ball; oldPos = ball.pos; p = ball.pos + ball.vel * dt })
        |> Array.ofList

    let halfW = platform.width / 2.0f
    let halfH = platform.height / 2.0f
    let left = platform.pos.X - halfW
    let right = platform.pos.X + halfW
    let top = platform.pos.Y - halfH
    let bottom = platform.pos.Y + halfH

    for _ in 1 .. iterations do
        // 小球与小球之间碰撞
        for i = 0 to stateArray.Length - 2 do
            for j = i + 1 to stateArray.Length - 1 do
                let s1 = stateArray.[i]
                let s2 = stateArray.[j]
                let delta = s1.p - s2.p
                let dist = delta.Length()
                let minDist = s1.ball.radius + s2.ball.radius
                if dist < minDist then
                    let penetration = minDist - dist
                    let correction =
                        if dist > 0.0f then (delta / dist) * (penetration * 0.5f)
                        else Vector2(penetration * 0.5f, 0.0f)
                    stateArray.[i] <- { s1 with p = s1.p + correction }
                    stateArray.[j] <- { s2 with p = s2.p - correction }

        // 小球与平台之间碰撞
        for i = 0 to stateArray.Length - 1 do
            let s = stateArray.[i]
            let closestX = clamp s.p.X left right
            let closestY = clamp s.p.Y top bottom
            let diff = s.p - Vector2(closestX, closestY)
            let diffLenSq = diff.LengthSquared()
            if diffLenSq < s.ball.radius * s.ball.radius then
                let diffLen = if diffLenSq = 0.0f then 0.0f else MathF.Sqrt(diffLenSq)
                let penetration = s.ball.radius - diffLen
                let normal = if diffLen = 0.0f then Vector2(0.0f, -1.0f) else diff / diffLen
                stateArray.[i] <- { s with p = s.p + normal * penetration }

    // 更新速度 + 旋转扰动（轻量化）
    let random = System.Random()

    stateArray
    |> Array.map (fun s ->
        let newVel = (s.p - s.oldPos) / dt
        let moved = Vector2.DistanceSquared(s.p, s.oldPos) > 0.0001f
        let angularImpulse = if moved then float32 (random.NextDouble() - 0.5) * 1.0f else 0.0f
        { s.ball with pos = s.p; vel = newVel; angularVel = s.ball.angularVel + angularImpulse }
    )
    |> Array.toList

/// 主循环入口
let Start() =
    let graphics = ManagerUtils.TryGetManager<GraphicsManager> ()
    let textRenderer = ManagerUtils.TryGetManager<TextManager> ()
    let inputDeviceManager = ManagerUtils.TryGetManager<InputDeviceInterface> ()

    let window = graphics.OpenWindow (Windowed (500u,500u)) "2D Physics with Rotation + Friction"

    let atlas =
        File.Open("Assets/ballCollisionTest2.png", FileMode.Open)
        |> window.LoadImage

    let ballImage =
        atlas.SubImage (Rectangle(Point(0, 0), Size(44, 44)))

    let platformImage =
        Some (atlas.SubImage (Rectangle(Point(0, 480), Size(500, 20))))

    let font = textRenderer.LoadFont window "Assets/Basic.fnt"

    let platform = {
        pos = Vector2(250.0f, 490.0f)
        width = 500.0f
        height = 20.0f
        img = platformImage
    }

    let ballCount = 10
    let minX, maxX = 100, 400
    let minY, maxY = 50, 200
    let diameter = 44.0f

    let rec generateBalls count (existing: Ball list) =
        if count <= 0 then existing
        else
            let candidatePos = Vector2(float32(globalRandom.Next(minX, maxX)), float32(globalRandom.Next(minY, maxY)))
            let collides = existing |> List.exists (fun ball -> Vector2.Distance(ball.pos, candidatePos) < diameter)
            if collides then generateBalls count existing
            else
                let newBall = {
                    pos = candidatePos
                    vel = Vector2(0.0f, 0.0f)
                    radius = 22.0f
                    img = ballImage
                    angle = 0.0f
                    angularVel = 0.0f
                }
                generateBalls (count - 1) (newBall :: existing)

    let mutable balls = generateBalls ballCount []
    let mutable lastTime = DateTime.Now

    let rec logic (win: Window) =
        if win.IsOpen() && not (Key.IsKeyDown Key.ESC) then
            let currentTime = DateTime.Now
            let deltaMS = (currentTime - lastTime).Milliseconds
            if deltaMS > 10 then
                lastTime <- currentTime
                let deltaTime = float32 deltaMS

                // 更新小球物理状态 + 带地面摩擦的角速度阻尼
                balls <-
                    balls
                    |> List.map (fun ball ->
                        let newVel = ball.vel + Vector2(0.0f, gravity * deltaTime)
                        let newPos = ball.pos + newVel * deltaTime

                        // 地面接触判断（简化）：底部贴近平台上沿
                        let touchingPlatform =
                            ball.pos.Y + ball.radius >= platform.pos.Y - (platform.height / 2.0f) - 1.0f

                        // 旋转阻尼系数：在平台上更快衰减
                        let damping = if touchingPlatform then 0.9f else 0.98f
                        let newAngularVel = ball.angularVel * damping
                        let newAngle = ball.angle + newAngularVel * deltaTime

                        { ball with pos = newPos; vel = newVel; angle = newAngle; angularVel = newAngularVel }
                    )

                // 使用 PBD 更新约束响应
                balls <- pbdSolveBalls balls platform deltaTime 5

                // 绘制阶段
                win.Clear (Color.Black)

                match platform.img with
                | Some img ->
                    let xform =
                        win.TranslationTransform platform.pos.X platform.pos.Y
                        |> fun t -> t.Multiply (win.TranslationTransform (-img.Size.X / 2.0f) (-img.Size.Y / 2.0f))
                    win.DrawImage xform img
                | None -> ()

                balls |> List.iter (fun ball ->
                    let xform =
                        win.TranslationTransform ball.pos.X ball.pos.Y
                        |> fun t -> t.Multiply (win.RotationTransform ball.angle)
                        |> fun t -> t.Multiply (win.TranslationTransform (-ball.img.Size.X / 2.0f) (-ball.img.Size.Y / 2.0f))
                    win.DrawImage xform ball.img
                )

                let fpsStr = "fps: " + (1000 / deltaMS).ToString()
                font.MakeText fpsStr
                |> fun txt -> txt.Draw win win.IdentityTransform

            win.Show()
            logic win

    window.Start(logic)
