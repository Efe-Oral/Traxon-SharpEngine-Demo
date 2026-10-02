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

Left mouse drag rotates the camera, Ctrl + drag moves it, the wheel zooms.

## How it works

A facade has fixtures, and every fixture has a few pixels. A pixel is one small light with its own color.

Making one scene object per pixel would be far too slow with millions of them. So the pixels are
drawn with instancing: the engine gets one quad mesh plus a list of positions and colors, and draws
the whole list in one go. In SharpEngine that is an `InstancedMeshNode`.

Pixels are flat quads instead of spheres because a quad is only 2 triangles, and from a distance
a light looks like a flat dot anyway. They are drawn in a solid color with no shading, so they
look like they glow.

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


| Fixtures | Pixels | Avg frame time | FPS |
| -------- | ------ | -------------- | --- |
| 5        | 10     | 2.43 ms        | 144 |


FPS can't go above 144 because of the screen, so frame time is the number to watch as the
fixture count grows.

## Progress

**Stage 1 (done):** 5 fixtures, 2 pixels each, nothing moving. The whole facade is 2 scene nodes:
one for the fixture housings and one for the pixels.

**Stats overlay (done):** fixture count, frame time and FPS in the top left corner. Added before
scaling up, so I can see what each change costs.

Next up:

- more fixtures (50, 500, thousands)
- animated colors
- click a fixture to select it
- add fixtures at runtime

