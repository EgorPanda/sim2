namespace SimulationCore;

public static class Num
{
    public static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
}
