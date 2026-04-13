// Shared vertex displacement hook for Test21.
// Tests that hook functions defined in #include files are found
// and registered correctly by the pipeline.

void displaceVertex(inout Attributes input)
{
    input.positionOS.xyz += input.normalOS * 0.01;
}
