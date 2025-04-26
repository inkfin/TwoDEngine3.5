namespace PhysicsObjectFactory

open System
open System.Numerics
open System.Drawing
open TDE3ManagerInterfaces.CollisionManagerInterface
open TDE3ManagerInterfaces.GraphicsManagerInterface

/// Define the ball data structure, including position, velocity, angle, radius, image, and collider
type Ball = {
    pos: Vector2
    vel: Vector2
    radius: float32
    angle: float32
    angularVel: float32
    collider: CollisionGeometry
    img: Image
}

/// Define the platform data structure
type Platform = {
    pos: Vector2
    width: float32
    height: float32
    collider: CollisionGeometry
    img: Image option
}

module Factory =

    // Random number utility function
    let random = Random()

    let RandomFloat (low: float) (high: float) =
        random.NextDouble() * (high - low) + low

    /// Generate a platform object with a rectangle collider
    let generatePlatform (x: float32) (y: float32) (width: float32) (height: float32) (img: Image option) =
        {
            pos = Vector2(x, y)
            width = width
            height = height
            collider = RectangleCollider {
                Position = Vector2(x, y)
                Width = width
                Height = height
            }
            img = img
        }

    /// Generate multiple ball objects, ensuring no initial overlap
    let generateBalls (count: int) (img: Image) (minX: int) (maxX: int) (minY: int) (maxY: int) : Ball list =
        let diameter = float32 img.Size.X
        let radius = diameter / 2.0f

        let rec loop (remaining: int) (acc: Ball list) : Ball list =
            if remaining = 0 then acc
            else
                let pos = Vector2(float32 (random.Next(minX, maxX)), float32 (random.Next(minY, maxY)))
                let tooClose = acc |> List.exists (fun b -> Vector2.Distance(b.pos, pos) < diameter)
                if tooClose then loop remaining acc
                else
                    let newBall: Ball = {
                        pos = pos
                        vel = Vector2.Zero
                        radius = radius
                        angle = 0.0f
                        angularVel = 0.0f
                        collider = CircleCollider {
                            Center = pos
                            Radius = radius
                        }
                        img = img
                    }
                    loop (remaining - 1) (newBall :: acc)

        loop count []
