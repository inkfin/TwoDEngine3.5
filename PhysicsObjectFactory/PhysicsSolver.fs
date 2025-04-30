namespace PhysicsSolver

open System
open System.Numerics
open TDE3ManagerInterfaces.CollisionManagerInterface
open PhysicsObjectFactory
open PhysicsObjectFactory.Factory
open TracyProfiler

type ISolver =
    abstract member Step: Ball list -> Platform -> float32 -> Ball list

/// Fixed timestep physics solver implementing ISolver
type FixedStepSolver(fixedDt: float32, maxSteps: int, iterations: int) =
    interface ISolver with
        member this.Step (balls: Ball list) (platform: Platform) (dt: float32) : Ball list =
            // Physics constants
            let gravity = 980f
            let restitution = 0.8f
            let airDamping = 0.999f
            let maxVelocity = 1000.0f
            let maxAngularVelocity = 500.0f
            let rollingFrictionCoefficient = 0.01f
            let staticFrictionThreshold = 0.05f

            // Utility functions
            let isValidVector (v: Vector2) =
                not (Single.IsNaN(v.X) || Single.IsNaN(v.Y) || Single.IsInfinity(v.X) || Single.IsInfinity(v.Y))

            let ensureValidVector (v: Vector2) (fallback: Vector2) =
                if isValidVector v then v else fallback

            let clamp (v: float32) (minVal: float32) (maxVal: float32) =
                if v < minVal then minVal elif v > maxVal then maxVal else v

            // One fixed simulation step
            let solveStep (balls: Ball list) (platform: Platform) : Ball list =
                let gravityForce = Vector2(0.0f, gravity * fixedDt)

                let state: (Ball * Vector2 * Vector2 * Vector2)[] =
                    balls
                    |> List.map (fun b ->
                        let vel = b.vel + gravityForce
                        let predicted = b.pos + vel * fixedDt
                        (b, b.pos, predicted, vel)
                    )
                    |> Array.ofList

                let halfW = platform.width / 2.0f
                let halfH = platform.height / 2.0f
                let left = platform.pos.X - halfW
                let right = platform.pos.X + halfW
                let top = platform.pos.Y - halfH
                let bottom = platform.pos.Y + halfH

                for _ in 1 .. iterations do
                    // Ball-ball resolution
                    for i = 0 to state.Length - 2 do
                        for j = i + 1 to state.Length - 1 do
                            let (b1, _, p1, _) = state.[i]
                            let (b2, _, p2, _) = state.[j]
                            let delta = p1 - p2
                            let dist = delta.Length()
                            let minDist = b1.radius + b2.radius
                            if dist < minDist && dist > 0.0001f then
                                let correction = (delta / dist) * ((minDist - dist) * 0.5f)
                                let newP1 = ensureValidVector (p1 + correction) p1
                                let newP2 = ensureValidVector (p2 - correction) p2
                                state.[i] <- (b1, b1.pos, newP1, state.[i] |> fun (_,_,_,v) -> v)
                                state.[j] <- (b2, b2.pos, newP2, state.[j] |> fun (_,_,_,v) -> v)

                    // Ball-platform resolution
                    for i = 0 to state.Length - 1 do
                        let (b, oldPos, p, v) = state.[i]
                        let closestX = clamp p.X left right
                        let closestY = clamp p.Y top bottom
                        let diff = p - Vector2(closestX, closestY)
                        let lenSq = diff.LengthSquared()
                        if lenSq < b.radius * b.radius then
                            let len = if lenSq < 0.0001f then 0.0f else MathF.Sqrt(lenSq)
                            let penetration = b.radius - len
                            let normal = if len < 0.0001f then Vector2(0.0f, -1.0f) else diff / len
                            let correction = normal * penetration
                            let newP = ensureValidVector (p + correction) p
                            state.[i] <- (b, oldPos, newP, v)

                // Apply final velocities and friction
                state
                |> Array.map (fun (b, oldP, newP, _) ->
                    let newVel = (newP - oldP) / fixedDt
                    let newVelClamped =
                        if newVel.LengthSquared() > maxVelocity * maxVelocity then
                            newVel * (maxVelocity / newVel.Length())
                        else newVel

                    // friction(roll)
                    let tangentVel = newVelClamped.X / b.radius * 180.0f / MathF.PI
                    let newAngularVel =
                        if abs newVelClamped.X > 0.01f then
                            let diff = tangentVel - b.angularVel
                            b.angularVel + diff * 0.1f
                        else
                            b.angularVel * (1.0f - rollingFrictionCoefficient)

                    let newAngularVel = clamp newAngularVel -maxAngularVelocity maxAngularVelocity
                    let newAngle = b.angle + newAngularVel * fixedDt

                    // friction(stat)
                    let frictionVel =
                        if newVelClamped.Length() < staticFrictionThreshold then Vector2.Zero
                        else Vector2(newVelClamped.X * 0.99f, newVelClamped.Y)

                    let finalAngularVel =
                        if abs newVelClamped.X < staticFrictionThreshold then 0.0f else newAngularVel

                    { b with pos = newP; vel = frictionVel; angularVel = finalAngularVel; angle = newAngle }
                )
                |> Array.toList

            // Fixed step loop
            let mutable remaining = dt
            let mutable steps = 0
            let mutable state = balls

            while remaining > 0.0f && steps < maxSteps do
                let step = if remaining < fixedDt then remaining else fixedDt
                use _ = Profiler.BeginEvent("Physics Substep")
                state <- solveStep state platform
                remaining <- remaining - step
                steps <- steps + 1

            state
