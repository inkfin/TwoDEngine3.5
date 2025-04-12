module CollisionTestScene

open System
open System.Numerics
open System.IO
open System.Drawing
open AngelCodeTextRenderer
open ImageExtensions
open InputManagerWinRawInput
open GraphicsManagerSFML
open SimpleCollisionManager
open TDE3ManagerInterfaces.CollisionManagerInterface
open TDE3ManagerInterfaces.InputDevices
open TDE3ManagerInterfaces.GraphicsManagerInterface
open TDE3ManagerInterfaces.TextRendererInterfaces
open Player  // 如果你在其他地方定义了 Key 或相关类型，请确保这里可引用到

// 用于产生随机数和生成随机浮点数
let Random = Random()
let RandomFloat (low: float) (high: float) =
    Random.NextDouble() * (high - low) + low

/// 定义小球类型，每个小球包含位置、速度、半径和图像信息
type Ball = {
    pos: Vector2
    vel: Vector2
    radius: float32
    img: Image  // 此处 Image 类型保持与项目现有代码一致
}

/// 定义平台类型（固定不动的长方形平台）
type Platform = {
    pos: Vector2       // 平台的中心点坐标
    width: float32
    height: float32
    img: Image option  // 可选的图像，用于背景展示；若为 None 可采用绘制矩形替代
}

/// 物理参数
let gravity = 0.001f      // 重力加速度（单位：像素/ms²）
let restitution = 0.8f    // 保留（本 PBD 版本中主要使用位置修正，不直接计算冲量）

/// --- 以下为 PBD 实现部分 ---

// 简单的 clamp 函数
let clamp (value: float32) (minVal: float32) (maxVal: float32) =
    if value < minVal then minVal
    elif value > maxVal then maxVal
    else value

/// 辅助类型，保存小球的旧位置及预测位置
type BallState = {
    ball: Ball
    oldPos: Vector2
    mutable p: Vector2 // 预测位置（通过积分计算得到，后续用于约束求解）
}

/// PBD 碰撞求解函数  
/// 参数：  
///   balls：待更新的小球列表  
///   platform：平台信息  
///   dt：时间步（单位 ms）  
///   iterations：求解迭代次数（通常 5～10 次可以取得较好效果）  
/// 返回：更新后的小球列表（位置和速度已调整）
let pbdSolveBalls (balls: Ball list) (platform: Platform) (dt: float32) (iterations: int) : Ball list =
    // 初始化：对于每个小球，用当前位置 + velocity×dt作为预测位置，记录旧位置
    let stateArray = 
        balls 
        |> List.map (fun ball -> { ball = ball; oldPos = ball.pos; p = ball.pos + ball.vel * dt })
        |> Array.ofList

    // 预先计算平台的边界（平台中心 pos, 左右上下一半尺寸）
    let halfW = platform.width / 2.0f
    let halfH = platform.height / 2.0f
    let left = platform.pos.X - halfW
    let right = platform.pos.X + halfW
    let top = platform.pos.Y - halfH
    let bottom = platform.pos.Y + halfH

    // 迭代求解碰撞约束
    for _ in 1 .. iterations do
        // 小球与小球之间的碰撞
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
                        if dist > 0.0f then
                            (delta / dist) * (penetration * 0.5f)
                        else
                            // 如果完全重合，选择 x 方向的一个任意矫正
                            Vector2(penetration * 0.5f, 0.0f)
                    stateArray.[i] <- { s1 with p = s1.p + correction }
                    stateArray.[j] <- { s2 with p = s2.p - correction }
        // 小球与平台之间的碰撞（平台被视为静止刚体）
        for i = 0 to stateArray.Length - 1 do
            let s = stateArray.[i]
            // 找出预测位置在平台矩形中的最近点（夹逼计算）
            let closestX = clamp s.p.X left right
            let closestY = clamp s.p.Y top bottom
            let diff = s.p - Vector2(closestX, closestY)
            let diffLenSq = diff.LengthSquared()
            if diffLenSq < s.ball.radius * s.ball.radius then
                let diffLen = if diffLenSq = 0.0f then 0.0f else MathF.Sqrt(diffLenSq)
                let penetration = s.ball.radius - diffLen
                let normal = if diffLen = 0.0f then Vector2(0.0f, -1.0f) else diff / diffLen
                stateArray.[i] <- { s with p = s.p + normal * penetration }

    // 更新小球的速度和位置，利用新的预测位置和旧位置计算出新的速度
    stateArray 
    |> Array.map (fun s ->
        let newVel = (s.p - s.oldPos) / dt
        { s.ball with pos = s.p; vel = newVel }
    )
    |> Array.toList

/// --- 主循环部分 ---
let Start() =
    // 获取各管理器
    let graphics = ManagerUtils.TryGetManager<GraphicsManager> ()
    let textRenderer = ManagerUtils.TryGetManager<TextManager> ()
    let inputDeviceManager = ManagerUtils.TryGetManager<InputDeviceInterface> ()
    // 注意：本 PBD 版不再使用 collisionMgr，故此处可省略 CollisionManager 的调用
    // let collision = ManagerUtils.TryGetManager<CollisionManager> ()
    
    // 打开一个 500x500 的窗口
    let window = graphics.OpenWindow (Windowed (500u,500u)) "2D Physics Demo"
  
    // 从图集加载资源（此处使用同一个图集，可根据实际图片资源调整对应的区域）
    let atlas =
        File.Open("Assets/ballCollisionTest.png", FileMode.Open)
        |> window.LoadImage

    // 假设图集中左上角区域为小球图片，尺寸44x44像素
    let ballImage =
        atlas.SubImage (
            Rectangle(
                Point(0, 0),
                Size(44, 44)
            )
        )
    // 平台图片（可选）；假设图集中 (0,480) 处有一块500x20的区域
    let platformImage =
        Some (atlas.SubImage (
                Rectangle(
                    Point(0, 480),
                    Size(500, 20)
                )
             ))
  
    // 载入字体，用于显示 FPS 等信息
    let font = textRenderer.LoadFont window "Assets/Basic.fnt"
    
    // 初始化平台：放置在窗口底部中间
    let platform =
        { pos = Vector2(250.0f, 490.0f)
          width = 500.0f
          height = 20.0f
          img = platformImage }

    // 生成小球时考虑大小，防止初始时小球重叠
    let ballCount = 10
    let minX = 100
    let maxX = 400
    let minY = 50
    let maxY = 200
    // 由于小球图像为 44×44，碰撞直径为 44 像素
    let diameter = 44.0f

    // 使用递归生成不重叠的小球列表
    let rec generateBalls count (existing: Ball list) =
        if count <= 0 then existing
        else
            let candidatePos = Vector2(float32(Random.Next(minX, maxX)), float32(Random.Next(minY, maxY)))
            // 检查新生成的小球与已有小球是否发生碰撞
            let collides = existing |> List.exists (fun ball -> Vector2.Distance(ball.pos, candidatePos) < diameter)
            if collides then
                generateBalls count existing
            else
                let newBall = { pos = candidatePos; vel = Vector2(0.0f, 0.0f); radius = 22.0f; img = ballImage }
                generateBalls (count - 1) (newBall :: existing)
    // 生成小球列表（顺序不影响后续逻辑）
    let mutable balls = generateBalls ballCount [] 

    // 记录上一次更新时间
    let mutable lastTime = DateTime.Now

    // 主循环：采用 PBD 来进行碰撞检测和位置约束求解
    let rec logic (win: Window) =
        if win.IsOpen() && not (Key.IsKeyDown Key.ESC) then
            let currentTime = DateTime.Now
            let deltaMS = (currentTime - lastTime).Milliseconds
            if deltaMS > 10 then
                lastTime <- currentTime
                let deltaTime = float32(deltaMS)

                // 更新每个小球：先施加重力、积分计算预测位置
                balls <-
                    balls
                    |> List.map (fun ball ->
                        let newVel = ball.vel + Vector2(0.0f, gravity * deltaTime)
                        let newPos = ball.pos + newVel * deltaTime
                        { ball with vel = newVel; pos = newPos }
                    )
                
                // 用 PBD 求解方式重新计算碰撞约束：
                // 此处我们设置迭代 5 次，数值可根据需要调整
                balls <- pbdSolveBalls balls platform deltaTime 5

                // 绘制
                win.Clear (Color.Black)

                // 绘制平台（若有平台图像，则使用图像；否则可扩展调用绘制纯色矩形函数）
                match platform.img with
                | Some img ->
                    let xform =
                        win.TranslationTransform platform.pos.X platform.pos.Y
                        |> fun t -> t.Multiply (win.TranslationTransform (-img.Size.X / 2.0f) (-img.Size.Y / 2.0f))
                    win.DrawImage xform img
                | None -> ()
                
                // 绘制所有小球
                balls |> List.iter (fun ball ->
                    let xform =
                        win.TranslationTransform ball.pos.X ball.pos.Y
                        |> fun t -> t.Multiply (win.TranslationTransform (-ball.img.Size.X / 2.0f) (-ball.img.Size.Y / 2.0f))
                    win.DrawImage xform ball.img
                )
                
                // 显示 FPS 信息
                let fpsStr = "fps: " + (1000 / deltaMS).ToString()
                font.MakeText fpsStr
                |> fun txt -> txt.Draw win win.IdentityTransform

            win.Show()
            logic win   // 递归调用

    // 启动主循环，注意传入的 lambda 参数必须符合 Window -> unit 的签名
    window.Start(logic)
