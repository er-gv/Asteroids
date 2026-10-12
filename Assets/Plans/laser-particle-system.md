# Project Overview
- **Game Title:** Asteroids 2D
- **High-Level Concept:** A classic 2D space arcade shooter where the player navigates a spaceship in a zero-gravity environment, rotating and thrusting with momentum while shooting lasers to blast floating asteroids and surviving without colliding with asteroids or screen boundaries.
- **Players:** Single-player.
- **Inspiration / Reference Games:** Atari Asteroids, Space Wars.
- **Tone / Art Direction:** Retro neon arcade with bright glowing laser visuals.
- **Target Platform:** Standalone PC (Windows 64-bit).
- **Screen Orientation / Resolution:** Landscape 1920x1080.
- **Render Pipeline:** Universal Render Pipeline (URP).

# Game Mechanics
## Core Gameplay Loop
1. Navigate the ship in zero-gravity space using physics thrust.
2. Aim at incoming asteroids and fire laser beams.
3. Holding the fire button continuously shoots bursts of $k$ lasers spaced by a configurable interval delay ($1.0\text{s}$ delay by default).
4. Lasers travel in straight trajectories until colliding with an asteroid (triggering split/destruction and score rewards) or the arena border (despawning cleanly), or until lifetime expires.
5. Clear all asteroid fragments to win the stage while avoiding ship-asteroid collisions.

## Controls and Input Methods
- **Fire Laser:** `Space` or `Enter` keyboard keys.
  - **Press / Single Tap:** Immediately fires a burst of $k$ laser particles.
  - **Hold Down:** Continuously fires bursts of $k$ laser particles every `fireInterval` seconds (e.g., $1.0\text{s}$ delay) as long as the button remains pressed.
  - **Release:** Halts firing immediately.

# UI
- **HUD:** Existing score display and game state UI.
- **Game Over / Victory Canvas:** Triggered on ship destruction or clearing all asteroids.

# Key Asset & Context
- `Assets/Scripts/PlayerController.cs`: Handles player movement, input callbacks (`OnFire(InputValue)`), and laser emission logic.
- `Assets/Scripts/Asteroid.cs`: Contains `OnParticleCollision(GameObject other)` which calls `Hit()` to split/destroy asteroids.
- `Assets/Scripts/Tags.cs`: Defines tag constants `Tags.Border` (`"t_border"`), `Tags.Asteroid` (`"t_asteroid"`), `Tags.Ship` (`"Player"`).
- `Assets/Prefabs/Player.prefab`: Player spaceship prefab containing `Rigidbody2D`, `PlayerController`, and `ParticleSystem`.
- `Assets/Materials/beam.mat`: Material asset for glowing laser beam rendering (URP Particles Unlit).

### Configurable Script Parameters in `PlayerController.cs`:
- `int kLaserCount`: Number of laser projectiles shot per fire event (default: 1).
- `float fireInterval`: Delay in seconds between successive laser shots while holding fire (default: 1.0s).
- `float laserSpeed`: Velocity of the laser projectile (e.g. 30 units/s).
- `float laserLifetime`: Maximum lifetime before expiring (e.g. 2.5s).
- `Vector2 laserSize`: Narrow width and elongated length for semi-rectangular beam (e.g. (0.15, 1.2)).
- `Color laserColor`: HDR glowing color with bloom intensity.

### Particle System Key Configuration:
- `Simulation Space`: `World` (ensures straight flight path independent of ship rotation).
- `Start Lifetime`: 2.5s.
- `Start Speed`: 30 units/s.
- `Collision Module (2D)`: Enabled, Type 2D, Collides with Asteroids & Borders, Lifetime Loss = 1.0, Send Collision Messages = true.
- `Renderer`: Stretched Billboard / Velocity aligned with `beam.mat`.

# Implementation Steps
### Step 1: Configure Laser Material
- **Description**: Configure `Assets/Materials/beam.mat` with URP Particles Unlit shader and bright HDR emission.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

### Step 2: Configure Player Particle System
- **Description**: Set up the ParticleSystem and ParticleSystemRenderer on `Assets/Prefabs/Player.prefab` with World simulation space, 2D collisions (Send Collision Messages = true, Lifetime Loss = 1), and velocity alignment.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

### Step 3: Implement Laser Weapon Logic in PlayerController
- **Description**: Update `Assets/Scripts/PlayerController.cs` with configurable parameters ($k$, `fireInterval`, `laserSpeed`, etc.) and continuous firing cadence while fire button is held.
- **Assigned role**: developer
- **Dependencies**: Step 2
- **Parallelizable**: No

### Step 4: Verify 2D Particle Collisions on Asteroids and Borders
- **Description**: Verify `Asteroid.cs` `OnParticleCollision` callback and border collisions despawn particles cleanly and award score on asteroid hit.
- **Assigned role**: developer
- **Dependencies**: Step 3
- **Parallelizable**: No

# Verification & Testing
1. **Single Shot Test**: Press Space/Enter once. Verify exactly $k$ bright, narrow semi-rectangular laser pulses are emitted forward in a straight line.
2. **Continuous Hold-Down Test**: Hold Space/Enter. Verify laser pulses fire at 1.0s intervals with clear separation, and stop upon release.
3. **Trajectory Test**: Fire while moving and rotating ship. Verify lasers maintain straight world-space trajectories.
4. **Collision Test**: Fire at an asteroid. Verify laser despawns on contact and triggers asteroid hit/split.
5. **Border & Lifetime Test**: Fire into open space. Verify laser despawns on hitting arena border or after lifetime expires.
