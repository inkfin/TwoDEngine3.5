namespace PhysicsSolver

open System
open System.Numerics
open TDE3ManagerInterfaces.CollisionManagerInterface
open PhysicsObjectFactory

module Solver =

    // Define physics parameters
    let gravity = 0.001f                          // Gravitational acceleration (unit: px/ms²)
    let restitution = 0.8f                        // Coefficient of restitution
    let airDamping = 0.999f                       // Air damping
    let maxVelocity = 10.0f                       // Maximum linear velocity
    let maxAngularVelocity = 5.0f                 // Maximum angular velocity
    let rollingFrictionCoefficient = 0.002f       // Rolling friction coefficient
    let staticFrictionThreshold = 0.02f           // Static friction threshold: speeds below this are considered stationary

    // Check if a vector is valid
    let isValidVector (v: Vector2) =
        not (Single.IsNaN(v.X) || Single.IsNaN(v.Y) || Single.IsInfinity(v.X) || Single.IsInfinity(v.Y))

    // Ensure vector validity; if invalid, return fallback value
    let ensureValidVector (v: Vector2) (fallback: Vector2) =
        if isValidVector v then v else fallback

    // Clamp value within a specified range
    let clamp (v: float32) (minVal: float32) (maxVal: float32) =
        if v < minVal then minVal elif v > maxVal then maxVal else v

    /// Main physics solver function
    let solve (balls: Ball list) (platform: Platform) (dt: float32) (iterations: int) : Ball list =
        let gravityForce = Vector2(0.0f, gravity * dt)

        // Initialize state array: each element is (ball, old position, predicted position, current velocity)
        let state =
            balls
            |> List.map (fun b ->
                let vel = b.vel + gravityForce  // Apply gravity to velocity
                let predicted = b.pos + vel * dt   // Predict next position using new velocity
                (b, b.pos, predicted, vel)
            )
            |> Array.ofList

        // Calculate platform boundaries
        let halfW = platform.width / 2.0f
        let halfH = platform.height / 2.0f
        let left = platform.pos.X - halfW
        let right = platform.pos.X + halfW
        let top = platform.pos.Y - halfH
        let bottom = platform.pos.Y + halfH

        // Constraint iterations: resolve ball-ball and ball-platform overlaps
        for _ in 1 .. iterations do
            for i = 0 to state.Length - 2 do
                for j = i + 1 to state.Length - 1 do
                    let (b1, _, p1, _) = state.[i]
                    let (b2, _, p2, _) = state.[j]
                    let delta = p1 - p2
                    let dist = delta.Length()
                    let minDist = b1.radius + b2.radius
                    if dist < minDist && dist > 0.0001f then   // Overlapping detected, separate the balls
                        let correction = (delta / dist) * ((minDist - dist) * 0.5f)
                        let newP1 = ensureValidVector (p1 + correction) p1
                        let newP2 = ensureValidVector (p2 - correction) p2
                        state.[i] <- (b1, b1.pos, newP1, state.[i] |> fun (_,_,_,v) -> v)
                        state.[j] <- (b2, b2.pos, newP2, state.[j] |> fun (_,_,_,v) -> v)

            for i = 0 to state.Length - 1 do  // Resolve ball-platform overlaps
                let (b, oldPos, p, v) = state.[i]
                let closestX = clamp p.X left right
                let closestY = clamp p.Y top bottom
                let diff = p - Vector2(closestX, closestY)
                let lenSq = diff.LengthSquared()    // Check for overlap
                if lenSq < b.radius * b.radius then
                    let len = if lenSq < 0.0001f then 0.0f else MathF.Sqrt(lenSq)
                    let penetration = b.radius - len
                    let normal = if len < 0.0001f then Vector2(0.0f, -1.0f) else diff / len
                    let correction = normal * penetration
                    let newP = ensureValidVector (p + correction) p
                    state.[i] <- (b, oldPos, newP, v)

        // Calculate new velocities, angular velocities, and angles based on corrected positions
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
                        b.angularVel + diff * 0.1f // Simulate sliding friction causing rotation
                    else
                        b.angularVel * (1.0f - rollingFrictionCoefficient)

                let newAngularVel = clamp newAngularVel (-maxAngularVelocity) maxAngularVelocity
                let newAngle = b.angle + newAngularVel * dt

                // Apply static friction: completely stop when velocity is very low
                let finalVel = if newVelClamped.Length() < staticFrictionThreshold then Vector2.Zero else newVelClamped
                let finalAngularVel = if abs newVelClamped.X < staticFrictionThreshold then 0.0f else newAngularVel

                { b with pos = newP; vel = finalVel; angularVel = finalAngularVel; angle = newAngle }
            )
            |> Array.toList

        updated