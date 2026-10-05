# Lua mods

Mods are Lua scripts run by Modzarella Core inside the game. A mod is a folder:

```
mymod/
  mod.json     { "id": "mymod", "name": "My Mod", "version": "1.0.0", "author": "you", "description": "One line." }
  main.lua     runs when the game starts
  *.lua        other files, loaded with require("name")
  files/       models (.glb), sounds (.wav) and anything else the mod loads
```

Files under `files/` are installed next to `main.lua`, so a mod loads `files/sounds/boom.wav` as `audio.load("sounds/boom.wav")`.

## A whole mod

```lua
local power = setting.number{ name = "Jump power", default = 200, min = 50, max = 800, desc = "How hard the jump is." }
local key = setting.key{ name = "Super jump", default = "J", desc = "Launch yourself." }

local function jump()
  local me = game.player()
  if not me then return end
  game.unground(me)
  for _, p in ipairs(body.parts(me)) do
    if body.live(p) then p.rigidBody.velocity = Vector3.up * power.value end
  end
  toast("Wheee")
end

menu.button("Jump", jump)

function update()
  if key.down then jump() end
end
```

Settings, keys and buttons appear on the mod's page in the F1 menu. Press **Reload** there to rerun `main.lua` after editing it.

## Building blocks

You rarely start from zero. Core does the hard parts that several mods share, so a new vehicle, weapon or gadget is mostly a model, some tuning and a few calls:

| Block | What it handles | Calls | Used by |
|---|---|---|---|
| Seats | Puts a body on a seat in an upright or reclined pose, hands on a target, safe to get on and off | `body.seat` | BMW, Rocket Toilet |
| Vehicles | Registers anything driveable so the camera and other mods react to it | `game.add_vehicle`, `game.vehicles`, `game.set_driver` | BMW, Rocket Toilet, Euphoria |
| Holding things | Hands grip an object, arms relax, the body faces where you aim | `body.grip`, `body.set_hands_busy`, `body.heading` | Guns |
| Muscles | Bends limbs toward a direction or point, cheaply on many bodies | `body.align`, `body.reach` | Euphoria, Guns |
| Wounds and limbs | Marks wounds and turns a limb into a loose piece | `body.mark_wound`, `body.gib` | Euphoria |
| Models | Loads `.glb` files, finds wheels, scales to size | `model.load`, `model.clone` | BMW, Guns, Rocket Toilet |
| Camera | Follow targets, over-the-shoulder offset, zoom, first person, viewmodels | `camera.target`, `camera.shift`, `camera.fov`, `camera.hide_arms` | BMW, Guns, Rocket Toilet |
| Effects and sound | Particles, decals, materials, positional sound | `fx.*`, `mat.*`, `audio.*` | All |
| Events | Mods react to each other without depending on each other | `events.on`, `events.emit` | Guns and Euphoria |

The Rocket Toilet and Sports Cars are a good example: both are a model, a seat and a vehicle registration, with their own movement code on top.

## Callbacks

Define any of these as global functions. They only run during a round, in Play Offline.

| Function | When |
|---|---|
| `update(dt)` | Every frame |
| `fixed_update(dt)` | Every physics step (100 per second) |
| `late_update(dt)` | After every frame, after the camera moves |
| `draw()` | When the screen is drawn; use `ui.*` here |
| `on_round_start()` | A new round began; objects from the last one are gone |
| `on_part_hit(part, collision)` | A body part hit something |
| `on_unload()` | Before **Reload**; clean up what you spawned |

## Settings and menu

| Call | Returns |
|---|---|
| `setting.number{ name, default, min, max, desc, section, advanced }` | `.value` |
| `setting.toggle{ name, default, desc, section, advanced }` | `.value` |
| `setting.choice{ name, default, options = {...}, desc, section, advanced }` | `.value` |
| `setting.key{ name, default = "T", desc }` | `.down`, `.held`, `.label` |
| `menu.button(label, fn)` | |

`advanced = true` hides a setting under **More settings**. Key names are Unity key codes: `"T"`, `"Space"`, `"Alpha1"`, `"Mouse1"`, `"F2"`, `"LeftControl + E"`.

## Game

| Call | |
|---|---|
| `game.player()` | Your ragdoll, or `nil` |
| `game.ragdolls()` | Every ragdoll, bots included |
| `game.is_local(r)` | Is this ragdoll you? |
| `game.scale(r)` | Body height in game units (about 9). Size things from it |
| `game.unground(r, stun)` | Knock a ragdoll off its feet |
| `game.grounded(r)`, `game.seated(r)` | State checks |
| `game.counting_down()`, `game.paused()`, `game.menu_open()` | |
| `game.round_age()` | Seconds since the round started |
| `game.next_round()`, `game.restart_map()` | Load the next map, or the current one again |
| `game.add_vehicle(rigidbody, wheels)`, `game.is_vehicle(rb)` | Register a vehicle (and its wheel transforms) so other mods can react to it |
| `game.vehicles()` | Every registered vehicle: `{ body, wheels }` |
| `game.set_driver(r, rb)`, `game.driver()` | Who is driving, for the camera and other mods |

## Bodies

A ragdoll `r` has parts: `r.head`, `r.spine1` (pelvis), `r.spine2` (chest), `r.upperArmLeft`, `r.lowerArmLeft`, `r.handLeft`, the same on the right, `r.upperLegLeft`, `r.lowerLegLeft`, `r.footLeft` and the same on the right. Each part has `.rigidBody` and `.transform`.

| Call | |
|---|---|
| `body.parts(r)` | All parts |
| `body.live(part)` | Still attached and simulated |
| `body.align(part, tip, dir, strength, damping, hold)` | Turn a limb so `tip` points along `dir` |
| `body.reach(hand, lower, upper, point, strength, damping, hold)` | Reach an arm or leg towards a point |
| `body.last_hit(r)` | Last hard impact: `.part`, `.dir`, `.time` |
| `body.touching(r)` | Any part on the ground |
| `body.facing(r)` | Horizontal direction the chest faces |
| `body.dead(r)`, `body.set_dead(r, true)` | Dead bodies go limp and ignore input |
| `body.heading(r, dir)` | Make the body face `dir` (`nil` to release) |
| `body.arm_strength(r, 0.1)`, `body.arm_swing(r, false)`, `body.reach_items(r, false)` | Loosen the arms, e.g. to hold something |
| `body.grip(hand, rigidbody, anchor)` | Hold an object; returns the joint |
| `body.set_hands_busy(r, true)` | Tell other mods the hands are taken |
| `body.gib(r, part, parts)` | Copy a limb into a loose physics object |
| `body.below(part)`, `body.parent(part)`, `body.name(r, part)` | Limb tree helpers |
| `body.seat(object, seat_transform, "upright" or "reclined", hand_target)` | A seat; hands reach `hand_target` if given. `:Sit(r, velocity)`, `:Stand(velocity)` |
| `body.ignore(r, colliders, true)` | Stop a ragdoll colliding with your object |

`hold` (seconds) keeps a muscle pulling without calling it every step. Use it for anything that runs on many ragdolls.

## Objects

Unity's own types are available: `Vector3`, `Quaternion`, `Color`, `Mathf`, `Time`, `Physics`, `Random`, `GameObject`, `LayerMask`, `Ray`, `ForceMode` and more. Methods use a colon: `rb:AddForce(v)`.

| Call | |
|---|---|
| `vec(x, y, z)`, `euler(x, y, z)`, `rgb(r, g, b, a)` | Shortcuts |
| `new_object(name, parent)` | Empty object |
| `primitive("Cube", parent, keep_collider)` | Cube, Sphere, Capsule, Cylinder, Plane, Quad |
| `add(object, "Rigidbody")`, `get(object, "Light")` | Components by name |
| `components(object, "Collider")`, `children(object, "Renderer")` | All matching components, as a table |
| `list(array)` | Turn a C# array into a Lua table |
| `destroy(object, delay)`, `destroy_now(object)` | |
| `alive(object)` | `false` once Unity destroyed it. Use this instead of `if object then` |
| `find(name)` | Object by name |
| `after(seconds, fn)` | Run later |
| `rand(a, b)`, `randi(a, b)` | Random float, random integer (b excluded) |
| `log(...)`, `toast(text)` | Write to the log, show a message |

## Physics

| Call | |
|---|---|
| `physics.raycast(origin, dir, distance, mask, skip_body, skip_ragdolls)` | Closest hit or `nil`: `.point`, `.normal`, `.distance`, `.collider`, `.rigidbody` |
| `physics.raycast_all(origin, dir, distance, mask)` | All hits, nearest first |
| `physics.overlap(center, radius, mask)` | Colliders in a sphere |
| `physics.ground` | Layer mask for terrain and obstacles |
| `physics.part(collider)` | The ragdoll part it belongs to, or `nil` |
| `physics.on_hit(object, fn(collision))` | Called when your object hits something |
| `physics.on_touch(object, fn(collision))` | Called every step while touching |
| `physics.ignore(collider_a, collider_b)` | |

## Models, sound and effects

| Call | |
|---|---|
| `model.load("car.glb", length)` | A .glb from the mod folder, scaled to `length`: `.Body`, `.Wheels`, `.BodyBounds`, `.WheelCenters`, `.WheelRadius` |
| `model.clone(template, parent)` | A visible copy |
| `audio.load("sounds/x.wav")`, `audio.folder("sounds")` | 16-bit WAV files |
| `audio.source(object, { clip, loop, spatial, volume })` | A sound source |
| `audio.play(source, clip, volume, pitch)` | One shot, scaled by the game's volume |
| `audio.at(clip, position, volume, pitch)` | One shot at a point |
| `mat.solid(color, gloss, metal)`, `mat.unlit(texture, color)`, `mat.emissive(color, strength)` | Materials |
| `mat.blob(size, noise, seed)` | Soft round texture for particles and decals |
| `fx.particles(object, { lifetime, speed, size, color, gravity, rate, angle, collide, stretch, grow, fade, loop, material })` | A particle system; `{a, b}` means a random range |
| `fx.emit(ps, count)`, `fx.rate(ps, n)`, `fx.speed(ps, a, b)`, `fx.tint(ps, color)` | |
| `fx.decal(pool, max, point, normal, along, width, height, material, parent)` | Flat mark on a surface; the oldest go past `max` |

## Camera and input

| Call | |
|---|---|
| `camera.rig()` | The game camera: `.yaw`, `.pitch` |
| `camera.main()` | The Unity camera |
| `camera.target(transform)` | Follow something else; `nil` follows you again |
| `camera.shift(vec)`, `camera.fov(scale)` | Offset (right, up, forward) and zoom; reset to `Vector3.zero` and `1` |
| `camera.shake(amount, decay)`, `camera.first_person()` | |
| `camera.hide_arms(true)` | Hide your own arms in first person, for viewmodels |
| `camera.flying()` | True while the freecam (F3) is on |
| `camera.to_screen(point)` | Screen position for `ui`, or `nil` behind the camera |
| `input.key("w")`, `input.key_down("enter")` | Keys by name: `w`, `space`, `leftShift`, `leftCtrl`, `upArrow`… |
| `input.mouse(0)`, `input.mouse_down(1)` | 0 left, 1 right, 2 middle |
| `input.allowed()` | False while the menu is open, the game is paused or unfocused |

## Drawing

Call these from `draw()`. Coordinates are screen pixels from the top left.

| Call | |
|---|---|
| `ui.hud(title, hint)` | The standard bottom-right panel. Use it so mods look alike |
| `ui.text(text, x, y, w, h, { size, color, align, bold, mono })` | |
| `ui.rect(x, y, w, h, color, angle)`, `ui.texture(tex, x, y, w, h, color, angle)` | |
| `ui.bar(x, y, w, h, fraction, color)` | |
| `ui.width()`, `ui.height()` | |

## Talking to other mods

```lua
events.on("bullet_hit", function(part, point, dir, power) ... end)
events.emit("bullet_hit", part, point, dir, power)
```

Events in use: `bullet_hit(part, point, dir, power)` and `wheels_bloody(rigidbody, point)`. Emitting an event nobody listens to is fine, so mods never need each other installed.

## Speed

Every call between Lua and the game costs a few microseconds. Per frame that's fine. For work on every ragdoll every physics step, use `hold` on muscles and only decide every few steps, the way Euphoria's reactions do.
