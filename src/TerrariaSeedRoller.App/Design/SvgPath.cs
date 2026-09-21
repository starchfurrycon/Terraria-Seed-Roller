using System.Drawing.Drawing2D;
using System.Globalization;

namespace TerrariaSeedRoller.App.Design;

/// <summary>
/// A compact SVG path-data interpreter that turns icon artwork into a
/// <see cref="GraphicsPath"/>. It understands M, L, H, V, C, S, Q, T, A and Z in
/// both absolute and relative form, which is the complete grammar used by the
/// bundled icon set. Arcs are converted to cubic Beziers using the endpoint to
/// centre parameterisation from the SVG specification.
/// </summary>
internal static class SvgPath
{
    public static GraphicsPath Parse(string data)
    {
        GraphicsPath path = new() { FillMode = FillMode.Winding };
        if (string.IsNullOrWhiteSpace(data)) return path;

        double curX = 0, curY = 0, startX = 0, startY = 0;
        double ctrlX = 0, ctrlY = 0;
        char command = '\0';
        char previous = '\0';
        int i = 0;

        while (true)
        {
            SkipSeparators(data, ref i);
            if (i >= data.Length) break;

            char c = data[i];
            if (char.IsLetter(c))
            {
                command = c;
                i++;
            }
            else if (command == '\0')
            {
                throw new FormatException("SVG path data must begin with a command letter.");
            }
            else if (previous is 'M' or 'm')
            {
                // Repeated coordinate pairs after a move command are implicit line-to commands.
                command = previous == 'M' ? 'L' : 'l';
            }

            bool relative = char.IsLower(command);
            double ox = relative ? curX : 0;
            double oy = relative ? curY : 0;

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    double x = ReadNumber(data, ref i) + ox;
                    double y = ReadNumber(data, ref i) + oy;
                    path.StartFigure();
                    curX = startX = x;
                    curY = startY = y;
                    break;
                }

                case 'L':
                {
                    double x = ReadNumber(data, ref i) + ox;
                    double y = ReadNumber(data, ref i) + oy;
                    AddLine(path, ref curX, ref curY, x, y);
                    break;
                }

                case 'H':
                {
                    double x = ReadNumber(data, ref i) + ox;
                    AddLine(path, ref curX, ref curY, x, curY);
                    break;
                }

                case 'V':
                {
                    double y = ReadNumber(data, ref i) + oy;
                    AddLine(path, ref curX, ref curY, curX, y);
                    break;
                }

                case 'C':
                {
                    double x1 = ReadNumber(data, ref i) + ox;
                    double y1 = ReadNumber(data, ref i) + oy;
                    double x2 = ReadNumber(data, ref i) + ox;
                    double y2 = ReadNumber(data, ref i) + oy;
                    double x = ReadNumber(data, ref i) + ox;
                    double y = ReadNumber(data, ref i) + oy;
                    AddBezier(path, ref curX, ref curY, x1, y1, x2, y2, x, y);
                    ctrlX = x2;
                    ctrlY = y2;
                    break;
                }

                case 'S':
                {
                    double x2 = ReadNumber(data, ref i) + ox;
                    double y2 = ReadNumber(data, ref i) + oy;
                    double x = ReadNumber(data, ref i) + ox;
                    double y = ReadNumber(data, ref i) + oy;
                    (double rx, double ry) = Reflect(previous, curX, curY, ctrlX, ctrlY);
                    AddBezier(path, ref curX, ref curY, rx, ry, x2, y2, x, y);
                    ctrlX = x2;
                    ctrlY = y2;
                    break;
                }

                case 'Q':
                {
                    double x1 = ReadNumber(data, ref i) + ox;
                    double y1 = ReadNumber(data, ref i) + oy;
                    double x = ReadNumber(data, ref i) + ox;
                    double y = ReadNumber(data, ref i) + oy;
                    AddQuadratic(path, ref curX, ref curY, x1, y1, x, y);
                    ctrlX = x1;
                    ctrlY = y1;
                    break;
                }

                case 'T':
                {
                    double x = ReadNumber(data, ref i) + ox;
                    double y = ReadNumber(data, ref i) + oy;
                    (double rx, double ry) = Reflect(previous, curX, curY, ctrlX, ctrlY);
                    AddQuadratic(path, ref curX, ref curY, rx, ry, x, y);
                    ctrlX = rx;
                    ctrlY = ry;
                    break;
                }

                case 'A':
                {
                    double rx = ReadNumber(data, ref i);
                    double ry = ReadNumber(data, ref i);
                    double rotation = ReadNumber(data, ref i);
                    bool largeArc = ReadFlag(data, ref i);
                    bool sweep = ReadFlag(data, ref i);
                    double x = ReadNumber(data, ref i) + ox;
                    double y = ReadNumber(data, ref i) + oy;
                    AddArc(path, ref curX, ref curY, rx, ry, rotation, largeArc, sweep, x, y);
                    break;
                }

                case 'Z':
                {
                    path.CloseFigure();
                    curX = startX;
                    curY = startY;
                    break;
                }

                default:
                    throw new FormatException($"Unsupported SVG path command '{command}'.");
            }

            previous = command;
        }

        return path;
    }

    private static void AddLine(GraphicsPath path, ref double curX, ref double curY, double x, double y)
    {
        path.AddLine((float)curX, (float)curY, (float)x, (float)y);
        curX = x;
        curY = y;
    }

    private static void AddBezier(
        GraphicsPath path, ref double curX, ref double curY,
        double x1, double y1, double x2, double y2, double x, double y)
    {
        path.AddBezier(
            (float)curX, (float)curY,
            (float)x1, (float)y1,
            (float)x2, (float)y2,
            (float)x, (float)y);
        curX = x;
        curY = y;
    }

    private static void AddQuadratic(
        GraphicsPath path, ref double curX, ref double curY,
        double x1, double y1, double x, double y)
    {
        // Elevate the quadratic to an equivalent cubic.
        double c1x = curX + 2.0 / 3.0 * (x1 - curX);
        double c1y = curY + 2.0 / 3.0 * (y1 - curY);
        double c2x = x + 2.0 / 3.0 * (x1 - x);
        double c2y = y + 2.0 / 3.0 * (y1 - y);
        AddBezier(path, ref curX, ref curY, c1x, c1y, c2x, c2y, x, y);
    }

    private static (double X, double Y) Reflect(char previous, double curX, double curY, double ctrlX, double ctrlY)
    {
        char p = char.ToUpperInvariant(previous);
        if (p is 'C' or 'S' or 'Q' or 'T')
        {
            return (2 * curX - ctrlX, 2 * curY - ctrlY);
        }
        return (curX, curY);
    }

    private static void AddArc(
        GraphicsPath path, ref double curX, ref double curY,
        double rx, double ry, double rotationDegrees, bool largeArc, bool sweep, double x, double y)
    {
        if (rx == 0 || ry == 0 || (curX == x && curY == y))
        {
            AddLine(path, ref curX, ref curY, x, y);
            return;
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        double phi = rotationDegrees * Math.PI / 180.0;
        double cosPhi = Math.Cos(phi);
        double sinPhi = Math.Sin(phi);

        double dx2 = (curX - x) / 2.0;
        double dy2 = (curY - y) / 2.0;
        double x1p = cosPhi * dx2 + sinPhi * dy2;
        double y1p = -sinPhi * dx2 + cosPhi * dy2;

        double lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
        if (lambda > 1)
        {
            double scale = Math.Sqrt(lambda);
            rx *= scale;
            ry *= scale;
        }

        double numerator = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
        double denominator = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        double sign = largeArc != sweep ? 1.0 : -1.0;
        double coef = denominator <= 0 ? 0 : sign * Math.Sqrt(Math.Max(0, numerator / denominator));

        double cxp = coef * rx * y1p / ry;
        double cyp = coef * -ry * x1p / rx;
        double cx = cosPhi * cxp - sinPhi * cyp + (curX + x) / 2.0;
        double cy = sinPhi * cxp + cosPhi * cyp + (curY + y) / 2.0;

        double ux = (x1p - cxp) / rx;
        double uy = (y1p - cyp) / ry;
        double vx = (-x1p - cxp) / rx;
        double vy = (-y1p - cyp) / ry;

        double theta1 = Math.Atan2(uy, ux);
        double delta = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        else if (sweep && delta < 0) delta += 2 * Math.PI;

        int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2.0)));
        double step = delta / segments;

        for (int s = 0; s < segments; s++)
        {
            double t1 = theta1 + s * step;
            double t2 = t1 + step;
            double alpha = Math.Sin(step) * (Math.Sqrt(4 + 3 * Math.Tan(step / 2) * Math.Tan(step / 2)) - 1) / 3.0;

            (double p1x, double p1y) = PointOnEllipse(cx, cy, rx, ry, cosPhi, sinPhi, t1);
            (double p2x, double p2y) = PointOnEllipse(cx, cy, rx, ry, cosPhi, sinPhi, t2);
            (double d1x, double d1y) = DerivativeOnEllipse(rx, ry, cosPhi, sinPhi, t1);
            (double d2x, double d2y) = DerivativeOnEllipse(rx, ry, cosPhi, sinPhi, t2);

            AddBezier(
                path, ref curX, ref curY,
                p1x + alpha * d1x, p1y + alpha * d1y,
                p2x - alpha * d2x, p2y - alpha * d2y,
                p2x, p2y);
        }
    }

    private static (double X, double Y) PointOnEllipse(
        double cx, double cy, double rx, double ry, double cosPhi, double sinPhi, double t)
    {
        double cosT = Math.Cos(t);
        double sinT = Math.Sin(t);
        return (cx + rx * cosT * cosPhi - ry * sinT * sinPhi,
                cy + rx * cosT * sinPhi + ry * sinT * cosPhi);
    }

    private static (double X, double Y) DerivativeOnEllipse(
        double rx, double ry, double cosPhi, double sinPhi, double t)
    {
        double cosT = Math.Cos(t);
        double sinT = Math.Sin(t);
        return (-rx * sinT * cosPhi - ry * cosT * sinPhi,
                -rx * sinT * sinPhi + ry * cosT * cosPhi);
    }

    private static void SkipSeparators(string data, ref int i)
    {
        while (i < data.Length && (char.IsWhiteSpace(data[i]) || data[i] == ',')) i++;
    }

    private static bool ReadFlag(string data, ref int i)
    {
        SkipSeparators(data, ref i);
        if (i >= data.Length) throw new FormatException("Unexpected end of SVG path data.");
        char c = data[i];
        if (c is '0' or '1')
        {
            i++;
            return c == '1';
        }
        throw new FormatException($"Expected an arc flag but found '{c}'.");
    }

    private static double ReadNumber(string data, ref int i)
    {
        SkipSeparators(data, ref i);
        int start = i;
        if (i < data.Length && (data[i] == '+' || data[i] == '-')) i++;
        while (i < data.Length && char.IsDigit(data[i])) i++;
        if (i < data.Length && data[i] == '.')
        {
            i++;
            while (i < data.Length && char.IsDigit(data[i])) i++;
        }
        if (i < data.Length && (data[i] == 'e' || data[i] == 'E'))
        {
            int save = i;
            i++;
            if (i < data.Length && (data[i] == '+' || data[i] == '-')) i++;
            if (i < data.Length && char.IsDigit(data[i]))
            {
                while (i < data.Length && char.IsDigit(data[i])) i++;
            }
            else
            {
                i = save;
            }
        }
        if (i == start) throw new FormatException($"Expected a number at offset {start} in SVG path data.");
        return double.Parse(data.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
