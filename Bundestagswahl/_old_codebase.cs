#define FIXED_PARTY_ORDER

using System.Windows.Controls;

using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Reflection;
using System.Windows;
using System.Linq;
using System;

using Unknown6656.Generics;

namespace Bundestagswahl;


public partial class MainWindow
    : Window
{
    private readonly Dictionary<int, PollResult> _polls = [];
    private readonly PollFetcher _fetcher;


    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        update_polls(sender, e);
        sc_pollcnt_ValueChanged(sender, null);

        lvc_overview.AxisX =
        [
            new Axis
            {
                Title = "Parties",
                FontFamily = FontFamily,
                LabelFormatter =  _ => "",
                Labels = _parties.Select(p => p.Name).ToList(),
            }
        ];
        lvc_overview.AxisY =
        [
            new Axis
            {
                Title = "Percentage",
                FontFamily = FontFamily,
                // MaxValue = parties.Max(p => poll[p]) * 1.05,
                MaxValue = 0.4,
                LabelFormatter = v => $"{v * 100:F1} %"
            }
        ];
        lvc_history.DataClick += (_, point) => cb_polls.SelectedIndex = cb_polls.Items.Count - 1 - (int)point.X;
    }

    private async void update_polls(object sender, RoutedEventArgs e)
    {
        _polls.Clear();

        cb_polls.SelectedIndex = -1;
        cb_polls.Items.Clear();

        update_coalitions(null);

        int maxcount = (int)sc_pollcnt.Value;

        foreach (PollResult poll in (await _fetcher.FetchAsync()).OrderByDescending(poll => poll.Date).Take(maxcount))
        {
            int index = cb_polls.Items.Add(poll.Date.ToString("yyyy-MM-dd"));

            _polls[index] = poll;
        }

        Dictionary<Party, LineSeries> lines = [];
        PollResult[] opolls = [.. _polls.Values.OrderBy(poll => poll.Date)];

        foreach (Party party in _parties)
            lines[party] = new LineSeries
            {
                Title = party.Name,
                Stroke = party.Brush,
                Fill = Brushes.Transparent,
                FontFamily = FontFamily,
                Values = new ChartValues<double>(opolls.Select(poll => poll[party])),
                DataLabels = maxcount < 200,
                LineSmoothness = 0,
            };

        lvc_history.Series = [.. lines.Values];
        lvc_history.AxisX =
        [
            new Axis
            {
                Unit = 1,
                Title = "Date",
                FontFamily = FontFamily,
                LabelFormatter = v => $"{opolls[(int)v].Date:yyyy-MM-dd}",
            }
        ];
        lvc_history.AxisY =
        [
            new Axis
            {
                MinValue = 0,
                MaxValue = (from p in _polls.Values
                            from r in p.Results.Values
                            select r).Max() * 1.5,
                Title = "Percentage",
                FontFamily = FontFamily,
                LabelFormatter = v => Math.Round(v * 100).ToString()
            }
        ];

        cb_polls.Focus();
    }

    private void Cb_polls_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lvc_history.Series.FirstOrDefault(s => s is ColumnSeries) is ColumnSeries cs)
            lvc_history.Series.Remove(cs);

        lvc_distr.Sections.Clear();

        if (cb_polls.SelectedIndex != -1)
        {
            if (lvc_overview.Series is null)
                lvc_overview.Series = [];

            PollResult poll = _polls[cb_polls.SelectedIndex];
            int ind = 0;

            foreach (Party party in _parties)
            {
                if (lvc_overview.Series.Count <= ind)
                    lvc_overview.Series.Add(new ColumnSeries
                    {
                        Title = party.Name,
                        FontFamily = FontFamily,
                        Values = new ChartValues<double>(new[] { poll[party] }),
                        Fill = party.Brush,
                        DataLabels = true,
                    });
                else
                    lvc_overview.Series[ind].Values[0] = poll[party];

                ++ind;
            }

            double last = 0;
            //PollResult norm = poll.Normalized;

            foreach (Party party in Party.LeftToRight)
            {
                lvc_distr.Sections.Add(new AngularSection
                {
                    Fill = party.Brush,
                    FromValue = last,
                    ToValue = last + poll[party] * 100,
                });

                last += poll[party] * 100;
            }
            
            double[] vs = new double[_polls.Count];

            vs[vs.Length - 1 - cb_polls.SelectedIndex] = lvc_history.AxisY.Last().MaxValue;

            lvc_history.Series.Add(new ColumnSeries
            {
                Title = "Selected Poll",
                Stroke = Brushes.Red,
                Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xff, 0, 0)),
                Values = new ChartValues<double>(vs)
            });

            update_coalitions(poll);
        }
        else
        {
            lvc_overview.Series.Clear();

            update_coalitions(null);
        }

        _angulargauge_update.Invoke(lvc_distr, []);

        cb_polls.Focus();
    }

    private void update_coalitions(in PollResult? _poll)
    {
        foreach ((AngularGauge ctrl, Label perc, Label desc, _) in _coalitions)
        {
            ctrl.Sections.Clear();
            ctrl.Value = 0;
            perc.Content = "0.0 %";
            desc.Content = "";

            if (_poll is null)
                _angulargauge_update.Invoke(ctrl, []);
        }

        if (_poll is PollResult poll)
            foreach ((AngularGauge ctrl, Label perc, Label desc, Party[] parties) in  _coalitions)
            {
                Coalition coa = new(poll, parties);
                double last = 0;

                foreach (Party party in parties)
                {
                    double p = coa[party] * 100;

                    ctrl.Sections.Add(new AngularSection
                    {
                        FromValue = last,
                        ToValue = last + p,
                        Fill = party.Brush
                    });
                    last += p;
                }

                ctrl.Value = last;
                perc.Content = $"{last:F1} %";
                desc.Content = parties.StringJoin(", ");

                if (last < 100)
                    ctrl.Sections.Add(new AngularSection
                    {
                        FromValue = last,
                        ToValue = 100,
                        Fill = Brushes.White,
                    });

                _angulargauge_update.Invoke(ctrl, []);
            }
    }
}
