using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Fereastra aplicației și bucla principală (main game loop).
/// Față de laboratorul 3: o scenă cu mai multe obiecte, fiecare cu matricea lui model,
/// o cameră cu matricele view și projection, tastatura și mouse-ul.
/// </summary>
public class Game : GameWindow
{
    // Obiecte care NU depind de OpenGL pot fi create imediat.
    private readonly Camera _camera = new();

    // Obiectele care folosesc OpenGL se creează în OnLoad (abia atunci există contextul OpenGL).
    private Shader _shader = null!;
    private readonly List<SceneObject> _scene = new();
    private readonly List<Transform2D> _initialTransforms = new();   // pentru tasta R

    // Starea aplicației (se modifică în OnUpdateFrame, se folosește în OnRenderFrame).
    private int _selected;            // indexul obiectului selectat în _scene (tasta Tab)
    private bool _wireframe;          // contur (true) sau plin (false)
                                      
    private Vector2 _pivot;           // T4: Coordonatele pivotului în spațiul lumii.  
    private bool _hasPivot = false;   // T4: Indică dacă utilizatorul a stabilit un pivot prin clic dreapta.

    private SceneObject _satellite = null!;         // T5: Obiectul satelit, separat de lista obiectelor selectabile.
    private float _satelliteAngle = 0.0f;   // T5: Unghiul orbital acumulat în timp, exprimat în grade.

    // Contoare afișate în titlul ferestrei.
    private readonly string _baseTitle;
    private double _secondTimer;
    private int _frames, _lastFrames;
    private bool _titleDirty = true;

    public Game(int width, int height, string title)
        : base(GameWindowSettings.Default,
               new NativeWindowSettings
               {
                   ClientSize = new Vector2i(width, height),
                   Title = title,
                   APIVersion = new Version(3, 3),          // cerem OpenGL 3.3 ...
                   Profile = ContextProfile.Core,           // ... profil Core (fără modul imediat)
                   Flags = ContextFlags.ForwardCompatible
               })
    {
        _baseTitle = title;
        VSync = VSyncMode.On;   // swap-ul bufferelor așteaptă reîmprospătarea monitorului
    }

    /// <summary>Apelată O SINGURĂ DATĂ, după crearea ferestrei și a contextului OpenGL.</summary>
    protected override void OnLoad()
    {
        base.OnLoad();

        Console.WriteLine($"OpenGL  : {GL.GetString(StringName.Version)}");
        Console.WriteLine($"Renderer: {GL.GetString(StringName.Renderer)}");

        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        _camera.Resize(ClientSize.X, ClientSize.Y);

        _shader = Shader.FromFiles("Shaders/shader.vert", "Shaders/shader.frag");
        // T5: Satelitul este un triunghi mic, cu scara inițială 0.3.
        /*AddObject("satelit", Shapes.Triangle(),
                  new Transform2D
                  {
                      Scale = new Vector2(0.3f, 0.3f)
                  });*/

        // T5: Creăm satelitul separat, pentru a nu putea fi selectat cu Tab.
        _satellite = new SceneObject(
                    "satelit",
                    Shapes.Triangle(),
                    new Transform2D
                    {
                        Scale = new Vector2(0.3f, 0.3f)
                    }
        );

        // Scena: aceleași forme din laboratorul 3, acum plasate în lume prin Transform2D.
        // Depth (z) contează doar în perspectivă (tasta O, secțiunea 7.2).
        AddObject("pătrat", Shapes.Quad(),
                  new Transform2D { Position = new Vector2(-1.8f, -1.0f), Depth = -3.0f });
        // T1: Verificăm scalarea și rotația triunghiului.
        // T1: Transformările sunt aplicate prin matricea ModelMatrix.
        AddObject("triunghi", Shapes.Triangle(),
                  new Transform2D
                  {
                      Position = new Vector2(-1.2f, 1.0f)
                  });

        /*AddObject("triunghi", Shapes.Triangle(),
                  new Transform2D
                  {
                      Position = new Vector2(-1.2f, 1.0f),
                      Rotation = 45.0f,
                      Scale = new Vector2(1.5f, 0.5f)
                  });*/
        AddObject("hexagon", Shapes.RegularPolygon(6, 0.5f),
                  new Transform2D { Position = new Vector2(2.0f, 1.0f) });
        AddObject("cerc", Shapes.RegularPolygon(64, 0.5f),
                  new Transform2D { Position = new Vector2(0.8f, -0.6f), Depth = 2.0f });
    }

    /// <summary>Adaugă un obiect în scenă și reține transformarea lui inițială (pentru tasta R).</summary>
    private void AddObject(string name, Mesh mesh, Transform2D transform)
    {
        _scene.Add(new SceneObject(name, mesh, transform));
        _initialTransforms.Add(transform.Clone());
    }

    /// <summary>Apelată când se schimbă dimensiunea ferestrei.</summary>
    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);

        GL.Viewport(0, 0, e.Width, e.Height);   // zona ferestrei în care se desenează
        _camera.Resize(e.Width, e.Height);      // proiecția se adaptează la noul raport de aspect
    }

    /// <summary>Apelată la fiecare iterație a buclei: LOGICA aplicației (intrări, mișcare, timp).</summary>
    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        if (KeyboardState.IsKeyDown(Keys.Escape))
        {
            Close();
        }

        // --- Obiectul selectat: tastatura (secțiunea 8) ------------------------------

        float dt = (float)args.Time;

        _satelliteAngle = (_satelliteAngle + 90.0f * dt) % 360.0f;   // T5: Satelitul parcurge o orbită completă în 4 secunde (90°/secundă).
        SceneObject sel = _scene[_selected];

        // T2: Shift accelerează de 3 ori toate mișcările obiectului selectat.
        float speedMultiplier = KeyboardState.IsKeyDown(Keys.LeftShift) ? 3.0f : 1.0f;

        // Deplasarea obiectului cu săgețile.
        const float moveSpeed = 2.0f;

        if (KeyboardState.IsKeyDown(Keys.Left))
            sel.Transform.Position.X -= moveSpeed * dt * speedMultiplier;

        if (KeyboardState.IsKeyDown(Keys.Right))
            sel.Transform.Position.X += moveSpeed * dt * speedMultiplier;

        if (KeyboardState.IsKeyDown(Keys.Down))
            sel.Transform.Position.Y -= moveSpeed * dt * speedMultiplier;

        if (KeyboardState.IsKeyDown(Keys.Up))
            sel.Transform.Position.Y += moveSpeed * dt * speedMultiplier;

        // T2: Rotația obiectului cu Q și E.
        const float rotationSpeed = 90.0f; // grade pe secundă

        if (KeyboardState.IsKeyDown(Keys.Q))
            sel.Transform.Rotation += rotationSpeed * dt * speedMultiplier;

        if (KeyboardState.IsKeyDown(Keys.E))
            sel.Transform.Rotation -= rotationSpeed * dt * speedMultiplier;

        // T2: Scalarea uniformă cu Z și X.
        float scaleFactor = MathF.Pow(1.5f, dt * speedMultiplier);

        // Z = micșorare.
        if (KeyboardState.IsKeyDown(Keys.Z))
        {
            sel.Transform.Scale.X /= scaleFactor;
            sel.Transform.Scale.Y /= scaleFactor;
        }

        // X = mărire.
        if (KeyboardState.IsKeyDown(Keys.X))
        {
            sel.Transform.Scale.X *= scaleFactor;
            sel.Transform.Scale.Y *= scaleFactor;
        }

        // Limităm fiecare componentă a scalei între 0.1 și 10.
        sel.Transform.Scale.X = Math.Clamp(sel.Transform.Scale.X, 0.1f, 10.0f);
        sel.Transform.Scale.Y = Math.Clamp(sel.Transform.Scale.Y, 0.1f, 10.0f);

        // T2: Scalarea se face prin înmulțire, nu prin adunare,
        // deoarece modificarea trebuie să fie proporțională cu dimensiunea actuală a obiectului.

        if (KeyboardState.IsKeyPressed(Keys.Tab)) _selected = (_selected + 1) % _scene.Count;
        if (KeyboardState.IsKeyPressed(Keys.O))   _camera.IsPerspective = !_camera.IsPerspective;
        if (KeyboardState.IsKeyPressed(Keys.F))   _wireframe = !_wireframe;
        if (KeyboardState.IsKeyPressed(Keys.R))   ResetScene();

        if (KeyboardState.IsKeyPressed(Keys.Tab) || KeyboardState.IsKeyPressed(Keys.O) ||
            KeyboardState.IsKeyPressed(Keys.R))
        {
            _titleDirty = true;
        }

        // --- Obiectul selectat: mouse-ul (secțiunea 9) -------------------------------

        // T3: La clic stânga, mutăm obiectul selectat în poziția cursorului,
        // convertită din coordonate de ecran în coordonate de lume.
        if (MouseState.IsButtonPressed(MouseButton.Left))
        {
            _scene[_selected].Transform.Position = ScreenToWorld(MouseState.Position);
        }


        // T4: Clic dreapta stabilește pivotul în poziția cursorului.
        if (MouseState.IsButtonPressed(MouseButton.Right))
        {
            _pivot = ScreenToWorld(MouseState.Position);
            _hasPivot = true;
        }
        // T4: Cât timp P este apăsată, obiectul se rotește în jurul pivotului.
        if (_hasPivot && KeyboardState.IsKeyDown(Keys.P))
        {
            // Unghiul de rotație în grade, calculat în funcție de timpul dintre cadre.
            float angle = 90.0f * dt;

            // Convertim unghiul în radiani.
            float radians = MathHelper.DegreesToRadians(angle);

            // Poziția obiectului față de pivot.
            Vector2 relativePosition = _scene[_selected].Transform.Position - _pivot;

            // Rotim poziția relativă în jurul pivotului.
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);

            Vector2 rotatedPosition = new Vector2(
                relativePosition.X * cos - relativePosition.Y * sin,
                relativePosition.X * sin + relativePosition.Y * cos
            );

            // Actualizăm poziția obiectului.
            _scene[_selected].Transform.Position = _pivot + rotatedPosition;

            // Rotim și orientarea proprie a obiectului cu aceeași valoare.
            _scene[_selected].Transform.Rotation += 90.0f * dt;
        }

        // --- Camera: tastatura și rotița (secțiunea 6.2) -----------------------------

        float camSpeed = 3.0f / _camera.Zoom;                    // unități de lume pe secundă
        if (KeyboardState.IsKeyDown(Keys.A)) _camera.Position.X -= camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.D)) _camera.Position.X += camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.S)) _camera.Position.Y -= camSpeed * dt;
        if (KeyboardState.IsKeyDown(Keys.W)) _camera.Position.Y += camSpeed * dt;

        _camera.Zoom *= MathF.Pow(1.1f, MouseState.ScrollDelta.Y);   // un pas al rotiței = ±10 %
        _camera.Zoom = Math.Clamp(_camera.Zoom, 0.1f, 10.0f);

        // --- Titlul ferestrei: obiectul selectat, proiecția, cadre pe secundă --------

        _secondTimer += args.Time;
        if (_secondTimer >= 1.0)
        {
            _lastFrames = _frames;
            _frames = 0;
            _secondTimer -= 1.0;
            _titleDirty = true;
        }

        if (_titleDirty)
        {
            string projection = _camera.IsPerspective ? "perspectivă" : "ortografică";
            Title = $"{_baseTitle} | selectat: {_scene[_selected].Name} | proiecție {projection} | FPS: {_lastFrames}";
            _titleDirty = false;
        }
    }

    /// <summary>Apelată la fiecare iterație a buclei: DESENAREA unui cadru.</summary>
    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        _frames++;

        // 1. Ștergem imaginea veche din back buffer.
        GL.ClearColor(0.12f, 0.14f, 0.18f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        // 2. Starea de rasterizare și programul shader.
        SetWireframe(_wireframe);
        _shader.Use();

        // 3. View și projection sunt aceleași pentru toate obiectele: le trimitem o singură dată.
        _shader.SetMatrix4("uView", _camera.ViewMatrix);
        _shader.SetMatrix4("uProjection", _camera.ProjectionMatrix);

        // 4. Fiecare obiect: matricea lui model, nuanța, apoi desenarea.
        foreach (SceneObject obj in _scene)
        {
            _shader.SetMatrix4("uModel", obj.Transform.ModelMatrix);
            _shader.SetColor("uTint", obj.Tint);
            obj.Mesh.Draw();
        }

        // T5: Calculăm matricea locală a satelitului:
        // scalare, deplasare la distanța 1.2, apoi rotație orbitală.
        float orbitRadians = MathHelper.DegreesToRadians(_satelliteAngle);

        Matrix4 satelliteLocalMatrix =
            _satellite.Transform.ModelMatrix
            * Matrix4.CreateTranslation(1.2f, 0.0f, 0.0f)
            * Matrix4.CreateRotationZ(orbitRadians);

        // T5: Aplicăm matricea model a obiectului selectat.
        // Astfel, satelitul urmărește toate transformările acestuia.
        Matrix4 satelliteModelMatrix =
            satelliteLocalMatrix * _scene[_selected].Transform.ModelMatrix;
        // T5: Când obiectul selectat este scalat, și distanța aparentă a satelitului
        // față de acesta se modifică, deoarece matricea ModelMatrix a obiectului
        // selectat este aplicată întregii transformări locale a satelitului.

        // Desenăm satelitul.
        _shader.SetMatrix4("uModel", satelliteModelMatrix);
        _shader.SetColor("uTint", _satellite.Tint);
        _satellite.Mesh.Draw();

        // 5. Afișăm cadrul terminat: back buffer <-> front buffer.
        SwapBuffers();
    }

    /// <summary>Apelată O SINGURĂ DATĂ, la închiderea ferestrei: eliberăm resursele GPU.</summary>
    protected override void OnUnload()
    {
        foreach (SceneObject obj in _scene)
        {
            obj.Dispose();
        }
        _shader.Dispose();

        base.OnUnload();
    }

    /// <summary>Transformă poziția mouse-ului (pixeli, origine stânga-sus) în coordonate de lume.</summary>

    private Vector2 ScreenToWorld(Vector2 mousePixels)
    {
        // T3: Pasul 1 - transformăm coordonatele mouse-ului din pixeli în NDC.
        // Originea mouse-ului este în stânga-sus, iar în NDC originea este în centru.
        float ndcX = 2.0f * mousePixels.X / ClientSize.X - 1.0f;
        float ndcY = 1.0f - 2.0f * mousePixels.Y / ClientSize.Y;

        // T3: Pasul 2 - construim matricea combinată View * Projection.
        Matrix4 viewProjection = _camera.ViewMatrix * _camera.ProjectionMatrix;

        // T3: Pasul 3 - calculăm inversa pentru a reveni din spațiul NDC în lumea scenei.
        Matrix4 inverseViewProjection = Matrix4.Invert(viewProjection);

        // T3: Determinăm adâncimea NDC corespunzătoare obiectului selectat.
        // Astfel, obiectul își păstrează coordonata Depth și ajunge sub cursor
        // inclusiv atunci când folosim proiecția în perspectivă.
        float depth = _scene[_selected].Transform.Depth;

        Vector4 referenceClip = Vector4.TransformRow(
            new Vector4(0.0f, 0.0f, depth, 1.0f),
            viewProjection);

        float ndcZ = referenceClip.Z / referenceClip.W;

        // T3: Pasul 4 - transformăm coordonatele NDC în coordonate de lume.
        Vector4 worldPosition = Vector4.TransformRow(
            new Vector4(ndcX, ndcY, ndcZ, 1.0f),
            inverseViewProjection);

        // T3: Împărțim la W pentru a obține coordonatele carteziene.
        return new Vector2(
            worldPosition.X / worldPosition.W,
            worldPosition.Y / worldPosition.W);
    }


    /// <summary>Tasta R: obiectele revin la transformările inițiale, camera la starea inițială.</summary>
    private void ResetScene()
    {
        for (int i = 0; i < _scene.Count; i++)
        {
            _scene[i].Transform = _initialTransforms[i].Clone();
        }
        _camera.Reset();
    }

    /// <summary>
    /// Contur / plin. Dacă Visual Studio semnalează o eroare la primul parametru,
    /// folosiți aici tipul acceptat de versiunea voastră (vezi laboratorul 2, Problema 3):
    /// TriangleFace.FrontAndBack sau MaterialFace.FrontAndBack.
    /// </summary>
    private static void SetWireframe(bool wireframe)
    {
        GL.PolygonMode(TriangleFace.FrontAndBack, wireframe ? PolygonMode.Line : PolygonMode.Fill);
    }
}
