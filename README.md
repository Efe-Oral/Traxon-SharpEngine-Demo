# Facade Lighting Demo

A small WPF app that draws the light fixtures of a building facade with
[Ab4d.SharpEngine](https://www.ab4d.com/SharpEngine.aspx).

The real-world target is a facade with 20,000 to 50,000 fixtures and around 5 million pixels.
I'm building towards that in small steps and writing down what I learn here.

![5 fixtures with 2 pixels each](docs/stage1.png)

## Run it

You need Windows and the .NET 10 SDK.

```
dotnet run
```

Camera: right mouse drag rotates, holding the mouse wheel and dragging moves it, scrolling zooms.

Selecting fixtures: hover to see a fixture's id, click to select one, drag to box select.
Hold Ctrl while clicking or dragging to add to the selection instead of replacing it.
`I` makes the selected fixtures blink for 2 seconds (identify), `Esc` clears the selection.

## How it works

A facade has fixtures, and every fixture has a few pixels. A pixel is one small light with its own color.

Making one scene object per thing would be far too slow with millions of them. So the fixture
housings are drawn with instancing: the engine gets one box mesh plus a list of positions, and
draws the whole list in one go. In SharpEngine that is an `InstancedMeshNode`.

The pixels started out the same way, as instanced flat quads. They are now a `PixelsNode`, which
draws one small dot per position and keeps the colors in their own buffer, so changing colors is
much cheaper (see Optimisations below). Pixels ignore the scene lights and show their exact color,
so they look like they glow.

## Real light vs fake light

The pixels don't actually light anything up. They are colored quads, and the wall next to a red
pixel doesn't turn red.

I thought about making them real lights, but no engine can calculate millions of light sources in
real time. And for a preview it isn't needed: if I control the color of every pixel, I'm already
showing what the facade will look like. If it needs to feel more like light, a glow around the
pixels can be faked cheaply later.

This is an assumption on my side, and something I'd like to discuss.

## Performance

Measured on my laptop: RTX 2060 6GB, Intel Core i7-10750H 2.60GHz, 144 Hz screen.


| Fixtures | Pixels per fixture | Pixels    | Avg frame time | FPS    |
| -------- | ------------------ | --------- | -------------- | ------ |
| 5        | 2                  | 10        | 2.43 ms        | 144    |
| 500      | 4                  | 2,000     | 2.43 ms        | 144    |
| 5,000    | 8                  | 40,000    | 2,49 ms        | 144    |
| 20,000   | 100                | 2,000,000 | 4,69 ms        | 144    |
| 50,000   | 100                | 5,000,000 | 9,65 ms        | 100,93 |


FPS can't go above 144 because of the screen, so frame time is the number to watch as the
fixture count grows.

### With animated colors

Every frame each pixel asks the current effect for its color, and the whole pixel array is sent
to the graphics card again. "Color update" is the time that takes. The engine's frame time
doesn't include it, so I measure it separately.


| Fixtures | Pixels    | Effect  | Color update | Avg frame time | FPS |
| -------- | --------- | ------- | ------------ | -------------- | --- |
| 5        | 10        | Wipe    | 0.02 ms      | 2.25 ms        | 144 |
| 500      | 2,000     | Wipe    | 0.08 ms      | 2.52 ms        | 140 |
| 5,000    | 40,000    | Wipe    | 2.02 ms      | 1.11 ms        | 144 |
| 20,000   | 2,000,000 | Wipe    | 60.2 ms      | 7.48 ms        | 14  |
| 50,000   | 5,000,000 | Wipe    | 204.6 ms     | 14.74 ms       | 5   |
| 20,000   | 2,000,000 | Rainbow | 91.4 ms      | 11.58 ms       | 9   |
| 50,000   | 5,000,000 | Rainbow | 247.9 ms     | 18.64 ms       | 4   |


Drawing 5 million pixels is fine (about 100 FPS static), but changing them every frame isn't.
Up to 40,000 pixels animation is basically free. At millions of pixels the color update takes
far longer than the drawing itself, and FPS drops to single digits.

This is the simple version on purpose: one C# loop over every pixel, then re-sending the whole
array, including positions that never change. Each pixel entry is 80 bytes but only 16 of them
are the color. Next step is finding out which part is slow and fixing that.

### Where the color update time goes

I split the color update in two: the C# loop that works out every pixel's color, and sending the
array to the graphics card (`UpdateInstancesData`).


| Pixels    | Effect  | Colors loop | Send to GPU | Total    |
| --------- | ------- | ----------- | ----------- | -------- |
| 40,000    | Wipe    | 0.75 ms     | 1.34 ms     | 2.09 ms  |
| 2,000,000 | Wipe    | 26.5 ms     | 32.7 ms     | 59.2 ms  |
| 2,000,000 | Rainbow | 48.4 ms     | 32.7 ms     | 81.1 ms  |
| 5,000,000 | Wipe    | 64.6 ms     | 135.2 ms    | 199.9 ms |
| 5,000,000 | Rainbow | 131.5 ms    | 141.2 ms    | 272.7 ms |


Both parts are slow at millions of pixels, so both need fixing. Sending is the bigger one and
doesn't depend on the effect, it's just the size of the array. The loop depends on how much math
the effect does.

### How often colors need to change

Right now the colors are recalculated on every drawn frame, so up to 144 times a second on my
screen. Before picking a lower rate I looked into DMX, the protocol real fixtures are controlled
with. A full DMX universe (all 512 channels) refreshes at about 44 Hz. With fewer channels it can
go faster, but a pixel facade packs its universes full (one RGB pixel is 3 channels, so about 170
pixels per universe), so 44 Hz is the realistic number here. Updating the preview's colors faster than ~44 times a second doesn't show
anything the building would show, so that's the rate I'm going for.

### Optimisations, before and after

Three fixes, from easiest to hardest. Each cell is color update time / FPS.

| Step                         | 2M pixels, Wipe  | 5M pixels, Wipe   | 5M pixels, Rainbow |
| ---------------------------- | ---------------- | ----------------- | ------------------ |
| Start (simple version)       | 59 ms / 15 fps   | 200 ms / 5 fps    | 273 ms / 3 fps     |
| 1. Parallel color loop       | 57 ms / 15 fps   | 185 ms / 5 fps    | 186 ms / 5 fps     |
| 2. Colors updated at 44 Hz   | 56 ms / 16 fps   | 186 ms / 5 fps    | not measured       |
| 3. Pixels as a `PixelsNode`  | 14 ms / 37 fps   | 43 ms / 14 fps    | 77 ms / 9 fps      |

**1. Parallel color loop.** The loop now runs on all CPU cores with `Parallel.For`. That's safe
because every pixel only reads its own position and writes its own color. It helped the heavy
Rainbow effect a lot, Wipe less, because after that the loop is mostly waiting on memory.

**2. Colors at 44 Hz.** The colors are recalculated at most 44 times a second (the DMX rate above),
and the frames in between just redraw. It does nothing for the biggest facades yet, because one
update there takes longer than 1/44 of a second anyway. It does help in the middle: at 500,000
pixels FPS went from 56 to 96.

**3. Only send the colors.** This was the big one. I looked inside SharpEngine and found that
`UpdateInstancesData` throws away the GPU buffer and builds a new 400 MB one on every call,
positions included. SharpEngine also has a `PixelsNode`, made for point clouds, which keeps
positions and colors in separate buffers. The positions go to the GPU once, and after that only
the colors are sent (16 bytes per pixel instead of 80). The pixels are now drawn as small dots
with a fixed size on screen instead of quads. From a distance it looks the same, and dots are
closer to what a real light point looks like anyway. I went with size 3, which keeps small gaps
between neighbouring pixels so moving light still reads as separate points.

Result: 2 million pixels now animate at close to the full 44 Hz, and 5 million pixels went from
5 to about 14 FPS. What's left at 5 million is mostly the color loop itself. I'd look at cheaper
effect math or doing the effects on the GPU next, but for this demo I stopped here.

## Progress

**Stage 1 (done):** 5 fixtures, 2 pixels each, nothing moving. The whole facade is 2 scene nodes:
one for the fixture housings and one for the pixels.

**Stats overlay (done):** fixture count, frame time and FPS in the top left corner. Added before
scaling up, so I can see what each change costs.

**Scaling (done):** fixtures are laid out in a grid, and the counts can be passed on the command
line: `dotnet run -c Release -- 50000 100` (fixtures, pixels per fixture).

**Effects (done):** pixel colors come from an effect. An effect only answers "what color is the
pixel at this spot on the facade, at this time?", where the spot goes from 0 to 1 across the
facade. That way the same effect works on any facade size, and video can plug in the same way
later. Two effects so far, Wipe and Rainbow.

Keys: `E` next effect, left / right speed, up / down brightness, space camera rotation.

**Optimisations (done):** see the before and after table above.

Next up:

- click a fixture to select it
- add fixtures at runtime

