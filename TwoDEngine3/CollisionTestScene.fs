module CollisionTestScene
open System
open System.Numerics
open System.IO
open AngelCodeTextRenderer
open ImageExtensions
open InputManagerWinRawInput
open GraphicsManagerSFML
open SimpleCollisionManager
open TDE3ManagerInterfaces.CollisionManagerInterface
open TDE3ManagerInterfaces.InputDevices
open TDE3ManagerInterfaces.GraphicsManagerInterface
open TDE3ManagerInterfaces.TextRendererInterfaces
open System.Drawing
open NewtonianObject
open Player

type SysColor = System.Drawing.Color

(*   Simple 2D Collision Test Scene *)

let Random = Random()
let RandomFloat (low:float) (high:float) =
   Random.NextDouble() * (high - low) + low



/// The Main Loop
let Start() =
    // Thes registrations will eventually be automatic runtims plugins
     //Register GraphicsManager
   
    let graphics = ManagerUtils.TryGetManager<GraphicsManager> ()
    let textRenderer = ManagerUtils.TryGetManager<TextManager> ()
    let inputDeviceManager = ManagerUtils.TryGetManager<InputDeviceInterface> ()
    let collision = ManagerUtils.TryGetManager<CollisionManager> ()
    
    let window = graphics.OpenWindow (Windowed (800u,600u)) "Asteroids"
  
    let atlas = 
        File.Open("Assets/asteroids-arcade.png", FileMode.Open)
        |>window.LoadImage
        
    let font =  textRenderer.LoadFont window "Assets/Basic.fnt"
    
    let shipImage =
        atlas.SubImage (
            Rectangle(
                Point(3, 2),
                Size(25, 30)
            )
         )
    let bulletImage =
        atlas.SubImage (
            Rectangle(
                Point(133, 75),
                Size(6, 6)
            )
         )    
    let explosionSheet = atlas.SubImage (
            Rectangle(
                Point(51, 142),
                Size(204, 36)
            )
         )    
    let bigAsteroid =
        atlas.SubImage (
            Rectangle(
                Point(66, 195),
                Size(60, 64)
            )
         )
      
                
    let logic (window:Window) =
        let startShipObj = {
            x=400.0f;y=300.0f;r=0f;
            vx=0.0f;vy=0.0f;vr=0f;img=shipImage}
        let mutable bulletList = List.empty;   
        let mutable PlayerObj = Ship {shipObject=startShipObj;lastFired = DateTime.Now }
        let mutable asteroids:NewtonianObject list =
                                    [ for i in 1..10 -> {
                                        x=float32(Random.Next(0,800))
                                        y=float32(Random.Next(0,600))
                                        r=float32(Random.Next(0,359))
                                        vx=float32(RandomFloat -0.05 0.05)
                                        vy=float32(RandomFloat -0.05 0.05)
                                        vr=float32(RandomFloat -0.1 0.1)
                                        img=bigAsteroid } ]
        let mutable explosion = AnimatedImage.createFromFrameCounts explosionSheet 3 1 (float 100) false
        let mutable lastTime = DateTime.Now
       
        while window.IsOpen() && not (Key.IsKeyDown Key.ESC) do
            let currentTime = DateTime.Now
            let deltaMS = (currentTime - lastTime).Milliseconds
          
            if deltaMS >10 then
               // Console.WriteLine ("deltaMS: " + deltaMS.ToString()) |> ignore
                lastTime <- currentTime
                // update state
                PlayerObj <- Player.Update PlayerObj deltaMS  bulletImage
                bulletList <- match PlayerObj with
                                |Ship ship ->
                                        let shipObject = ship.shipObject
                                        if (Player.CheckFireBullet ship) then
                                             PlayerObj <- Ship {ship with lastFired = DateTime.Now}
                                             { shipObject with
                                                vy= -cos(DegToRad(shipObject.r));vx= sin(DegToRad(shipObject.r))
                                                vr=0f;img=bulletImage}::bulletList
                                        else
                                            bulletList
                                 | _ -> bulletList
                bulletList <- NewtonianObject.NewtonianUpdateList deltaMS bulletList
                              |> NewtonianObject.RemoveOffScreen 800f 600f
                asteroids <- List.map (fun rock ->
                    NewtonianObject.NewtonianUpdate deltaMS rock |> NewtonianObject.Wrap 800f 600f) asteroids
                
                
                //display code
                window.Clear (SysColor.Black)  
              
                asteroids
                |>List.iter(fun (asteroid:NewtonianObject) ->
                    let xform =
                        window.TranslationTransform asteroid.x asteroid.y 
                        |> fun x -> x.Multiply (window.RotationTransform asteroid.r)
                        |> fun x -> x.Multiply
                                        (window.TranslationTransform
                                                (-asteroid.img.Size.X /2f)
                                                (-asteroid.img.Size.Y /2f) )
                    window.DrawImage xform asteroid.img )
                
                match PlayerObj with
                | Ship ship ->
                    //printfn $"{ship.shipObject.x} {ship.shipObject.y} {ship.shipObject.r}"
                    let xform =
                        window.TranslationTransform ship.shipObject.x ship.shipObject.y 
                        |> fun x -> x.Multiply (window.RotationTransform ship.shipObject.r)
                        |> fun x -> x.Multiply
                                        (window.TranslationTransform
                                                (-ship.shipObject.img.Size.X /2f)
                                                (-ship.shipObject.img.Size.Y /2f)) 
                    window.DrawImage xform ship.shipObject.img
                    bulletList
                    |> List.iter(
                            fun bullet ->
                                let bxform =
                                    window.TranslationTransform bullet.x bullet.y
                                    |> fun x -> x.Multiply (window.RotationTransform bullet.r)
                                    |> fun x -> x.Multiply
                                                    (window.TranslationTransform 
                                                            (-bullet.img.Size.X /2f)
                                                            (-bullet.img.Size.Y /2f))
                                window.DrawImage bxform bullet.img
                        )
                    
                | Explosion expl ->
                                  window.TranslationTransform
                                     ((float32 -expl.img.FrameWidth)/2f+expl.x)
                                     ((float32 -expl.img.FrameHeight)/2f+expl.y)
                                  |>AnimatedImage.draw expl.img window
                            // for asteroid in asteroids do
                | Dead -> () //Eventually draw game over screen          
                let fpsStr = "fps: "+ (1000/deltaMS).ToString()
                font.MakeText fpsStr
                |> fun x -> x.Draw window window.IdentityTransform
                
  
     // do collision detection           
                match PlayerObj with
                | Ship ship ->
                     let shipCollison =  CircleCollider { Center = Vector2 (ship.shipObject.x, ship.shipObject.y);
                                           Radius = (max ship.shipObject.img.Size.X ship.shipObject.img.Size.Y) / 2.0f} 
                //collision detection

                     asteroids
                     |> List.iter (fun asteroid ->
                              let asteroidCollision = CircleCollider {Center= Vector2 (asteroid.x, asteroid.y);
                                                       Radius = (max asteroid.img.Size.X asteroid.img.Size.Y) / 2.0f} 
                              match collision.Collide shipCollison asteroidCollision with
                              | Some result ->  PlayerObj <- Explosion {x=ship.shipObject.x;y=ship.shipObject.y;img=explosion}
                                                printfn "Ship hit" |> ignore
                              | None ->
                                  bulletList
                                  |> List.fold (fun removalListsTuple bullet ->
                                        let bulletCollision = CircleCollider {Center= Vector2 (bullet.x, bullet.y);
                                                                   Radius = (max bullet.img.Size.X bullet.img.Size.Y) / 2.0f} 
                                        asteroids
                                        |> List.tryFind(fun asteroid ->
                                             let asteroidCollision = CircleCollider {
                                                                   Center= Vector2 (asteroid.x, asteroid.y);
                                                                   Radius =
                                                                       float32 (max asteroid.img.Size.X
                                                                                    asteroid.img.Size.Y) / 2.0f}
                                             match collision.Collide bulletCollision asteroidCollision with
                                             | Some result ->  true
                                             | None -> false                      
                                        )
                                        |> function
                                           |Some asteroid ->
                                                printfn ("Hit asteroid\n")
                                                (bullet:: fst removalListsTuple, asteroid:: snd removalListsTuple)
                                           |None -> removalListsTuple
                                    ) (List.Empty,List.Empty)
                                  |> fun removalListsTuple ->
                                        match PlayerObj with
                                        | Ship ship ->
                                            asteroids <- asteroids
                                                        |> List.except (snd removalListsTuple)
                                            bulletList <- bulletList
                                                        |> List.except (fst removalListsTuple)
                                        |_ -> ()
                                      ) |> ignore
                          
                                        
                 | _ -> ()
                window.Show()  
               
        window.Close()
        ()
            
    window.Start(logic)
