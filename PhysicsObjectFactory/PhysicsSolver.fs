namespace PhysicsSolver

open System
open System.Numerics
open TDE3ManagerInterfaces.CollisionManagerInterface
open PhysicsObjectFactory

module Solver =

    // 物理参数定义
    let gravity = 0.001f                          // 重力加速度（单位: px/ms²）
    let restitution = 0.8f                        // 弹性系数
    let airDamping = 0.999f                       // 空气阻尼
    let maxVelocity = 10.0f                       // 最大线速度限制
    let maxAngularVelocity = 5.0f                 // 最大角速度限制
    let rollingFrictionCoefficient = 0.002f       // 滚动摩擦系数
    let staticFrictionThreshold = 0.02f           // 静摩擦阈值：低于这个速度将被视为完全静止

    // 向量有效性检查
    let isValidVector (v: Vector2) =
        not (Single.IsNaN(v.X) || Single.IsNaN(v.Y) || Single.IsInfinity(v.X) || Single.IsInfinity(v.Y))

    // 确保向量有效，若无效则返回备用值
    let ensureValidVector (v: Vector2) (fallback: Vector2) =
        if isValidVector v then v else fallback

    // 限制数值在范围内
    let clamp (v: float32) (minVal: float32) (maxVal: float32) =
        if v < minVal then minVal elif v > maxVal then maxVal else v

    /// 主物理求解函数
    let solve (balls: Ball list) (platform: Platform) (dt: float32) (iterations: int) : Ball list =
        let gravityForce = Vector2(0.0f, gravity * dt)

        // 初始化状态数组：每个元素是 (球, 旧位置, 预测位置, 当前速度)
        let state =
            balls
            |> List.map (fun b ->
                let vel = b.vel + gravityForce  // 应用重力后的新速度
                let predicted = b.pos + vel * dt   // 用这个速度预测下一帧位置
                (b, b.pos, predicted, vel)
            )
            |> Array.ofList

        // 平台边界计算
        let halfW = platform.width / 2.0f
        let halfH = platform.height / 2.0f
        let left = platform.pos.X - halfW
        let right = platform.pos.X + halfW
        let top = platform.pos.Y - halfH
        let bottom = platform.pos.Y + halfH

        // 约束迭代：处理球与球、球与平台之间的重叠修复
        for _ in 1 .. iterations do
            for i = 0 to state.Length - 2 do
                for j = i + 1 to state.Length - 1 do
                    let (b1, _, p1, _) = state.[i]
                    let (b2, _, p2, _) = state.[j]
                    let delta = p1 - p2
                    let dist = delta.Length()
                    let minDist = b1.radius + b2.radius
                    if dist < minDist && dist > 0.0001f then   //发生了重叠,将小球隔开保证距离
                        let correction = (delta / dist) * ((minDist - dist) * 0.5f)
                        let newP1 = ensureValidVector (p1 + correction) p1
                        let newP2 = ensureValidVector (p2 - correction) p2
                        state.[i] <- (b1, b1.pos, newP1, state.[i] |> fun (_,_,_,v) -> v)
                        state.[j] <- (b2, b2.pos, newP2, state.[j] |> fun (_,_,_,v) -> v)

            for i = 0 to state.Length - 1 do  //球与平台之间的重叠修复
                let (b, oldPos, p, v) = state.[i]
                let closestX = clamp p.X left right
                let closestY = clamp p.Y top bottom
                let diff = p - Vector2(closestX, closestY)
                let lenSq = diff.LengthSquared()    //判断是否重叠
                if lenSq < b.radius * b.radius then
                    let len = if lenSq < 0.0001f then 0.0f else MathF.Sqrt(lenSq)
                    let penetration = b.radius - len
                    let normal = if len < 0.0001f then Vector2(0.0f, -1.0f) else diff / len
                    let correction = normal * penetration
                    let newP = ensureValidVector (p + correction) p
                    state.[i] <- (b, oldPos, newP, v)

        // 根据修正后的位置计算新速度、角速度与旋转角度
        let updated =
            state
            |> Array.map (fun (b, oldP, newP, _) ->
                let newVel = (newP - oldP) / dt
                let newVelClamped =
                    if newVel.LengthSquared() > maxVelocity * maxVelocity then
                        newVel * (maxVelocity / newVel.Length())
                    else newVel

                let tangentVel = newVelClamped.X / b.radius * 180.0f / MathF.PI
                let newAngularVel =
                    if abs newVelClamped.X > 0.01f then
                        let diff = tangentVel - b.angularVel
                        b.angularVel + diff * 0.1f // 模拟滑动摩擦导致旋转
                    else
                        b.angularVel * (1.0f - rollingFrictionCoefficient)

                let newAngularVel = clamp newAngularVel (-maxAngularVelocity) maxAngularVelocity
                let newAngle = b.angle + newAngularVel * dt

                // 应用静摩擦：速度非常小时彻底静止
                let finalVel = if newVelClamped.Length() < staticFrictionThreshold then Vector2.Zero else newVelClamped
                let finalAngularVel = if abs newVelClamped.X < staticFrictionThreshold then 0.0f else newAngularVel

                { b with pos = newP; vel = finalVel; angularVel = finalAngularVel; angle = newAngle }
            )
            |> Array.toList

        updated
