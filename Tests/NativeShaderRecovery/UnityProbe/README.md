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

Run `StructuredVertexProbe.Run` with the same arguments to verify a
12-byte buffer using real `SV_VertexID` inputs. It checks the bits of all
three loaded floats and the zero result for indices outside the logical
buffer count, through both Material and MaterialPropertyBlock bindings.

Unity 2021's Metal translator may speculate buffer reads across an early
return. The generated helper clamps the actual read index and requires the
host to bind a real buffer whose length is at least `max(count, 1)`, including
a one-element fallback when the logical buffer is null. The helper returns zero
for a zero count or an out-of-range logical index. The Metal compiler output
must be inspected as well as readback: a zero result alone does not prove that
an invalid read was avoided. D3D11 instead obtains the count from the buffer;
other backends currently fail closed.
