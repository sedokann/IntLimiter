using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace IntLimiter.Controls;

public class SparklineControl : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(Queue<double>), typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(SparklineControl),
            new FrameworkPropertyMetadata(Colors.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineThicknessProperty =
        DependencyProperty.Register(nameof(LineThickness), typeof(double), typeof(SparklineControl),
            new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

    public Queue<double>? Values
    {
        get => (Queue<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public double LineThickness
    {
        get => (double)GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var values = Values;
        if (values == null || values.Count < 2) return;

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var points = new List<double>(values);
        double max = 0;
        foreach (var p in points) if (p > max) max = p;
        if (max == 0) max = 1;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            double step = w / (points.Count - 1);
            for (int i = 0; i < points.Count; i++)
            {
                double x = i * step;
                double y = h - (points[i] / max) * h;
                if (i == 0)
                    ctx.BeginFigure(new Point(x, y), false, false);
                else
                    ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geometry.Freeze();

        var pen = new Pen(new SolidColorBrush(LineColor), LineThickness);
        pen.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }
}
