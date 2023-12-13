#define FIXED_PARTY_ORDER

using System.Windows.Controls;

using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Reflection;
using System.Windows;
using System.Linq;
using System;

using LiveCharts.Wpf;
using LiveCharts;

using Unknown6656.Generics;

namespace Bundestagswahl;


public partial class MainWindow
    : Window
{
    private const string CACHE_FILE = "cache.bin";

    private static readonly Party[] _exclude = [Party.PIRATEN, Party.FW];
    private static readonly Party[] _parties = Party.All.Except(_exclude).ToArray();
    private static readonly MethodInfo _angulargauge_update = typeof(AngularGauge).GetMethod("Draw", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly (AngularGauge ctrl, Label perc, Label desc, Party[] parties)[] _coalitions;
    private readonly Dictionary<int, PollResult> _polls = [];
    private readonly PollFetcher _fetcher;


    public MainWindow()
    {
        InitializeComponent();

        _fetcher = new(new(CACHE_FILE));
        _coalitions = new (AngularGauge, Label, Label, Party[])[]
        {
            (lvc_coa1, prc_coa1, lbl_coa1, [Party.CDU, Party.SPD]),
            (lvc_coa2, prc_coa2, lbl_coa2, [Party.CDU, Party.SPD, Party.FDP]),
            (lvc_coa3, prc_coa3, lbl_coa3, [Party.CDU, Party.SPD, Party.GRÜNE]),
            (lvc_coa4, prc_coa4, lbl_coa4, [Party.CDU, Party.FDP, Party.GRÜNE]),
            (lvc_coa5, prc_coa5, lbl_coa5, [Party.SPD, Party.LINKE, Party.GRÜNE]),
            (lvc_coa6, prc_coa6, lbl_coa6, [Party.CDU, Party.SPD, Party.FDP, Party.GRÜNE]),
            (lvc_coa7, prc_coa7, lbl_coa7, [Party.CDU, Party.AFD]),
            (lvc_coa8, prc_coa8, lbl_coa8, [Party.CDU, Party.FDP, Party.AFD]),
            (lvc_coa9, prc_coa9, lbl_coa9, [Party.AFD, Party.FDP]),
            (lvc_coa10, prc_coa10, lbl_coa10, [Party.SPD, Party.GRÜNE]),
            (lvc_coa11, prc_coa11, lbl_coa11, [Party.CDU, Party.FDP]),
            (lvc_coa12, prc_coa12, lbl_coa12, [Party.CDU, Party.GRÜNE]),
            (lvc_coa13, prc_coa13, lbl_coa13, [Party.GRÜNE, Party.LINKE]),
        };

        Loaded += MainWindow_Loaded;
    }

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

    private void sc_pollcnt_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double>? e)
    {
        int cnt = (int)sc_pollcnt.Value;

        if (lbl_pollcnt != null)
            lbl_pollcnt.Content = cnt.ToString();

        if (btn_fetchpolls != null)
            btn_fetchpolls.Content = $"Fetch the last {cnt} polls ...";
    }
}
