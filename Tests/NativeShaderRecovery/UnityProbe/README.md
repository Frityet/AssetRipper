# Synthetic structured-buffer probe

Run as a closed standalone Unity 2021.3.45f2 project on a Metal-capable Mac:

```
Unity -batchmode -projectPath /absolute/path/to/UnityProbe -executeMethod StructuredProbe.Run -quit -logFile /absolute/path/to/probe.log
```

The shader contains the same scalar guarded-load helper emitted by the fork.
It checks count-zero guarding, in/out-of-range indices, NaN, the large uint
conversion boundary, and both Material and integer-ID MaterialPropertyBlock
bindings through GPU readback. It contains no game input. This focused probe
is not a stock-game or full shader semantic acceptance test.
