namespace PhysicsObjectFactory

open System
open System.Numerics
open System.Drawing
open TDE3ManagerInterfaces.CollisionManagerInterface
open TDE3ManagerInterfaces.GraphicsManagerInterface

/// 定义小球数据结构，包含位置信息、速度、角度、半径、图片和碰撞体
type Ball = {
    pos: Vector2
    vel: Vector2
    radius: float32
    angle: float32
    angularVel: float32
    collider: CollisionGeometry
    img: Image
}

/// 定义平台数据结构
type Platform = {
    pos: Vector2
    width: float32
    height: float32
    collider: CollisionGeometry
    img: Image option
}

module Factory =

    // 随机数工具函数
    let random = Random()

    let RandomFloat (low: float) (high: float) =
        random.NextDouble() * (high - low) + low

    /// 生成平台对象，带矩形碰撞器
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

    /// 批量生成小球对象，保证初始时不重叠（使用显式类型标注避免推断问题）
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

