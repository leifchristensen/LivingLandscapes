using System;

namespace UnityEngine;

/// <summary>
/// The subset of UnityEngine.Mathf that the linked, game-free source files use, so they compile and run outside the game.
/// Add members only as a linked file needs them, with the same semantics as Unity's.
/// </summary>
public static class Mathf
{
    public const float PI = (float)Math.PI;

    public static float Abs(float v) => Math.Abs(v);
    public static float Min(float a, float b) => Math.Min(a, b);
    public static float Max(float a, float b) => Math.Max(a, b);
    public static int Min(int a, int b) => Math.Min(a, b);
    public static int Max(int a, int b) => Math.Max(a, b);
    public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    public static float Sqrt(float v) => (float)Math.Sqrt(v);
    public static float Pow(float v, float p) => (float)Math.Pow(v, p);
    public static float Exp(float v) => (float)Math.Exp(v);
    public static float Cos(float v) => (float)Math.Cos(v);
    public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
    public static int CeilToInt(float v) => (int)Math.Ceiling(v);
}
