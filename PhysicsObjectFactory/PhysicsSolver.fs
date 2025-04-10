namespace PhysicsSolver

open System
open System.Numerics
open TDE3ManagerInterfaces.CollisionManagerInterface
open PhysicsObjectFactory
open TracyProfiler

type BallState = {
    mutable ball: Ball
    mutable oldPos: Vector2
    mutable newPos: Vector2
    mutable velocity: Vector2
}

type ISolver =
    abstract member Step: Ball[] -> Platform -> float32 -> Ball[]

type FixedStepSolver(fixedDt: float32, maxSteps: int, iterations: int) =
    interface ISolver with
        member _.Step(balls: Ball[]) (platform: Platform) (dt: float32) : Ball[] =
            let gravity = 980f
            let restitution = 0.8f
            let airDamping = 0.999f
            let maxVelocity = 1000.0f
            let maxAngularVelocity = 500.0f
            let rollingFrictionCoefficient = 0.01f
            let staticFrictionThreshold = 0.05f

            let isValidVector (v: Vector2) =
                not (Single.IsNaN(v.X) || Single.IsNaN(v.Y) || Single.IsInfinity(v.X) || Single.IsInfinity(v.Y))

            let ensureValidVector (v: Vector2) (fallback: Vector2) =
                if isValidVector v then v else fallback

            let clamp (v: float32) (minVal: float32) (maxVal: float32) =
                if v < minVal then minVal elif v > maxVal then maxVal else v

            let solveStep (ballStates: BallState[]) =
                let gravityForce = Vector2(0.0f, gravity * fixedDt)

                for state in ballStates do
                    state.velocity <- state.ball.vel + gravityForce
                    state.oldPos <- state.ball.pos
                    state.newPos <- state.ball.pos + state.velocity * fixedDt

                let halfW = platform.width / 2.0f
                let halfH = platform.height / 2.0f
                let left = platform.pos.X - halfW
                let right = platform.pos.X + halfW
                let top = platform.pos.Y - halfH
                let bottom = platform.pos.Y + halfH

                for _ in 1 .. iterations do
                    for i = 0 to ballStates.Length - 2 do
                        for j = i + 1 to ballStates.Length - 1 do
                            let s1 = ballStates[i]
                            let s2 = ballStates[j]
                            let delta = s1.newPos - s2.newPos
                            let dist = delta.Length()
                            let minDist = s1.ball.radius + s2.ball.radius
                            if dist < minDist && dist > 0.0001f then
                                let correction = (delta / dist) * ((minDist - dist) * 0.5f)
                                s1.newPos <- ensureValidVector (s1.newPos + correction) s1.newPos
                                s2.newPos <- ensureValidVector (s2.newPos - correction) s2.newPos

                    for s in ballStates do
                        let p = s.newPos
                        let closestX = clamp p.X left right
                        let closestY = clamp p.Y top bottom
                        let diff = p - Vector2(closestX, closestY)
                        let lenSq = diff.LengthSquared()
                        if lenSq < s.ball.radius * s.ball.radius then
                            let len = if lenSq < 0.0001f then 0.0f else MathF.Sqrt(lenSq)
                            let penetration = s.ball.radius - len
                            let normal = if len < 0.0001f then Vector2(0.0f, -1.0f) else diff / len
                            let correction = normal * penetration
                            s.newPos <- ensureValidVector (s.newPos + correction) s.newPos

                for s in ballStates do
                    let newVel = (s.newPos - s.oldPos) / fixedDt
                    let newVelClamped =
                        if newVel.LengthSquared() > maxVelocity * maxVelocity then
                            newVel * (maxVelocity / newVel.Length())
                        else newVel

                    let tangentVel = newVelClamped.X / s.ball.radius * 180.0f / MathF.PI
                    let newAngularVel =
                        if abs newVelClamped.X > 0.01f then
                            let diff = tangentVel - s.ball.angularVel
                            s.ball.angularVel + diff * 0.1f
                        else
                            s.ball.angularVel * (1.0f - rollingFrictionCoefficient)

                    let newAngularVel = clamp newAngularVel -maxAngularVelocity maxAngularVelocity
                    let newAngle = s.ball.angle + newAngularVel * fixedDt

                    let frictionVel =
                        if newVelClamped.Length() < staticFrictionThreshold then Vector2.Zero
                        else Vector2(newVelClamped.X * 0.99f, newVelClamped.Y)

                    let finalAngularVel =
                        if abs newVelClamped.X < staticFrictionThreshold then 0.0f else newAngularVel

                    s.ball <- {
                        s.ball with
                            pos = s.newPos
                            vel = frictionVel
                            angle = newAngle
                            angularVel = finalAngularVel
                    }

            let mutable remaining = dt
            let mutable steps = 0
            let mutable ballStates =
                balls
                |> Array.map (fun b ->
                    {
                        ball = b
                        oldPos = b.pos
                        newPos = b.pos
                        velocity = b.vel
                    }
                )

            while remaining > 0.0f && steps < maxSteps do
                let step = if remaining < fixedDt then remaining else fixedDt
                use _ = Profiler.BeginEvent("Physics Substep")
                solveStep ballStates
                remaining <- remaining - step
                steps <- steps + 1

            ballStates |> Array.map (fun s -> s.ball)
