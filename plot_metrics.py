import pandas as pd
import numpy as np
import plotly.graph_objs as go
from dash import Dash, dcc, html, Input, Output, callback, State, no_update
import dash_bootstrap_components as dbc
from dash.exceptions import PreventUpdate
import base64
import io
from scipy.signal import medfilt
from dash import ctx

app = Dash(__name__, external_stylesheets=[dbc.themes.DARKLY])
server = app.server

app.layout = dbc.Container(
    [
        dbc.Row(dbc.Col(html.H1("Анализ метрик", className="text-center my-4"))),
        
        # Загрузка файлов
        dbc.Row(
            dbc.Col(
                [
                    dcc.Upload(
                        id='upload-data',
                        children=html.Div([
                            'Перетащите или ',
                            html.A('выберите CSV-файл')
                        ]),
                        style={
                            'width': '100%',
                            'height': '60px',
                            'lineHeight': '60px',
                            'borderWidth': '1px',
                            'borderStyle': 'dashed',
                            'borderRadius': '5px',
                            'textAlign': 'center',
                            'margin': '10px 0',
                            'backgroundColor': '#2c3e50'
                        },
                        multiple=True
                    ),
                    html.Div(id='file-info', className="text-light small mb-3"),
                ], 
                width=12
            )
        ),
        
        dbc.Row(
            [
                dbc.Col(
                    [
                        html.Label("Выберите метрики:", className="mb-2 text-light"),
                        dcc.Dropdown(
                            id='metric-selector',
                            options=[],
                            value=[],
                            multi=True,
                            className="mb-3"
                        ),
                        
                        html.Div(
                            [
                                html.Label("Степень сглаживания данных:", className="mb-2 text-light"),
                                dcc.Slider(
                                    id='smoothing',
                                    min=0,
                                    max=100,
                                    step=5,
                                    value=30,
                                    marks={0: '0', 25: '25', 50: '50', 75: '75', 100: '100'},
                                    className="mb-3"
                                ),
                                html.Div(id='smoothing-info', className="text-light small mb-3"),
                            ]
                        ),
                        
                        dbc.Button("Обновить графики", id="update-button", color="primary", className="mb-3"),
                        
                        html.Div(
                            [
                                html.Label("Настройки производных:", className="mb-2 text-light"),
                                dbc.Checklist(
                                    options=[{"label": "Игнорировать начальный скачок", "value": 1}],
                                    value=[1],
                                    id="deriv-settings",
                                    switch=True,
                                    className="mb-2"
                                ),
                                dbc.Checklist(
                                    options=[{"label": "Логарифмическая шкала", "value": 1}],
                                    value=[],
                                    id="log-scale",
                                    switch=True,
                                    className="mb-2"
                                ),
                            ], 
                            className="mt-3 p-3 border rounded bg-secondary"
                        ),
                        
                        html.Div(
                            [
                                html.Label("Снимок на TimeDelta:", className="text-light"),
                                dcc.Input(id='snapshot-time', type='number', placeholder="в секундах", className="mb-2"),
                                dbc.Button("Сгенерировать отчёт", id='snapshot-button', color="info", className="mb-2"),
                                html.Pre(id='snapshot-output', className="bg-dark text-light p-3 border rounded")
                            ],
                            className="mt-4"
                        ),
                        
                        html.Div(id='stats-output', className="mt-3 p-3 border rounded bg-dark text-light")
                    ], 
                    width=3
                ),
                
                dbc.Col(
                    [
                        dcc.Tabs(
                            [
                                dcc.Tab(
                                    label="Значения", 
                                    children=[dcc.Graph(id='metrics-plot', config={'displayModeBar': True})],
                                    className="text-light"
                                ),
                                dcc.Tab(
                                    label="Производные", 
                                    children=[dcc.Graph(id='derivatives-plot', config={'displayModeBar': True})],
                                    className="text-light"
                                )
                            ]
                        )
                    ], 
                    width=9
                )
            ]
        ),
        
        dcc.Store(id='all-raw-data'),
        dcc.Store(id='processed-data')
    ], 
    fluid=True, 
    className="bg-dark text-light"
)

# Обработка загрузки файла
def parse_contents(contents, filename):
    content_type, content_string = contents.split(',')
    decoded = base64.b64decode(content_string)

    try:
        try:
            df = pd.read_csv(io.StringIO(decoded.decode('utf-8')))
        except:
            df = pd.read_csv(io.StringIO(decoded.decode('cp1251')))

        if 'Timestamp' not in df.columns or 'Metric' not in df.columns or 'Value' not in df.columns:
            return None, "Файл должен содержать колонки: Timestamp, Metric, Value"

        df['Timestamp'] = pd.to_datetime(df['Timestamp'])
        min_time = df['Timestamp'].min()
        df['TimeDelta'] = (df['Timestamp'] - min_time).dt.total_seconds()

        return df, f"Успешно загружено: {filename}, строк: {len(df)}"

    except Exception as e:
        print(e)
        return None, f"Ошибка обработки файла: {str(e)}"

# Обновление информации о файле
@app.callback(
    [Output('file-info', 'children'),
     Output('metric-selector', 'options'),
     Output('metric-selector', 'value'),
     Output('all-raw-data', 'data')],
     Input('upload-data', 'contents'),
     State('upload-data', 'filename'),
     State('all-raw-data', 'data')
)
def update_file_info(contents, filenames, existing_data):
    if contents is None:
        raise PreventUpdate

    if not isinstance(contents, list):
        contents = [contents]
        filenames = [filenames]

    if existing_data is None:
        existing_data = []

    new_entries = []
    for content, name in zip(contents, filenames):
        df, message = parse_contents(content, name)
        if df is not None:
            new_entries.append({'filename': name, 'data': df.to_dict('records')})

    existing_data.extend(new_entries)

    all_metrics = pd.concat([pd.DataFrame(f['data']) for f in existing_data])
    metric_options = [{'label': m, 'value': m} for m in sorted(all_metrics['Metric'].unique())]
    default_value = [metric_options[0]['value']] if metric_options else []

    info_msg = f"Файлов загружено: {len(existing_data)}"
    return info_msg, metric_options, default_value, existing_data


# Информация о сглаживании
@app.callback(
    Output('smoothing-info', 'children'),
    Input('smoothing', 'value')
)
def update_smoothing_info(value):
    if value == 0:
        return "Сглаживание отключено"
    elif value < 30:
        return "Слабое сглаживание (сохраняются быстрые изменения)"
    elif value < 70:
        return "Умеренное сглаживание (баланс деталей и стабильности)"
    else:
        return "Сильное сглаживание (акцент на общих трендах)"

# Обработка данных
@app.callback(
    Output('processed-data', 'data'),
    [Input('update-button', 'n_clicks'),
     Input('metric-selector', 'value')],
    [State('smoothing', 'value'),
     State('deriv-settings', 'value'),
     State('all-raw-data', 'data')]
)
def process_data(n_clicks, selected_metrics, smoothing, deriv_settings, all_raw_data):
    if not all_raw_data or not selected_metrics:
        raise PreventUpdate

    results = []
    ignore_initial = 1 in deriv_settings if deriv_settings else False

    for entry in all_raw_data:
        df = pd.DataFrame(entry['data'])
        filename = entry['filename']

        for metric in selected_metrics:
            metric_df = df[df['Metric'] == metric].copy()
            if metric_df.empty:
                continue

            metric_df = metric_df.sort_values('TimeDelta')

            if ignore_initial and len(metric_df) > 10:
                initial_time = metric_df['TimeDelta'].iloc[0]
                metric_df = metric_df[metric_df['TimeDelta'] > initial_time + 5]

            if smoothing > 0 and len(metric_df) > 5:
                window_size = max(3, min(51, int(len(metric_df) * smoothing / 200)))
                if window_size % 2 == 0:
                    window_size += 1
                try:
                    metric_df['Smoothed'] = medfilt(metric_df['Value'], kernel_size=window_size)
                except:
                    metric_df['Smoothed'] = metric_df['Value']
            else:
                metric_df['Smoothed'] = metric_df['Value']

            time_diffs = metric_df['TimeDelta'].diff()
            value_diffs = metric_df['Smoothed'].diff()
            metric_df['Derivative'] = value_diffs / time_diffs
            metric_df.replace([np.inf, -np.inf], np.nan, inplace=True)

            if len(metric_df) > 10:
                deriv = metric_df['Derivative'].dropna()
                if not deriv.empty:
                    q05 = deriv.quantile(0.05)
                    q95 = deriv.quantile(0.95)
                    iqr = q95 - q05
                    metric_df['Derivative'] = metric_df['Derivative'].clip(q05 - 3*iqr, q95 + 3*iqr)

            metric_df['Source'] = filename
            results.append(metric_df)

    if not results:
        raise PreventUpdate

    return pd.concat(results).to_dict('records')

# Обновление графика значений
@app.callback(
    Output('metrics-plot', 'figure'),
    Input('processed-data', 'data'),
    State('metric-selector', 'value')
)
def update_metrics_plot(data, selected_metrics):
    if not data or not selected_metrics:
        return go.Figure()

    df_plot = pd.DataFrame(data)
    if df_plot.empty:
        return go.Figure()

    fig = go.Figure()

    for metric in selected_metrics:
        for source in df_plot['Source'].unique():
            metric_df = df_plot[(df_plot['Metric'] == metric) & (df_plot['Source'] == source)]

            if 'Smoothed' in metric_df.columns:
                fig.add_trace(go.Scatter(
                    x=metric_df['TimeDelta'],
                    y=metric_df['Smoothed'],
                    mode='lines',
                    name=f"{metric} ({source})"
                ))
                fig.add_trace(go.Scatter(
                    x=metric_df['TimeDelta'],
                    y=metric_df['Value'],
                    mode='markers',
                    marker=dict(size=3, opacity=0.3),
                    name=f"{metric} исходные ({source})",
                    showlegend=False
                ))

    fig.update_layout(
        template="plotly_dark",
        title="Метрики во времени",
        xaxis_title="Время (сек)",
        yaxis_title="Значение",
        legend_title="Метрика (файл)",
        hovermode="x unified"
    )
    return fig

# Обновление графика производных
@app.callback(
    Output('derivatives-plot', 'figure'),
    [Input('processed-data', 'data'),
     Input('log-scale', 'value'),
     State('metric-selector', 'value')]
)
def update_derivatives_plot(data, log_scale, selected_metrics):
    if not data or not selected_metrics:
        return go.Figure()

    df_plot = pd.DataFrame(data)
    if df_plot.empty:
        return go.Figure()

    fig = go.Figure()

    for metric in selected_metrics:
        for source in df_plot['Source'].unique():
            metric_df = df_plot[(df_plot['Metric'] == metric) & (df_plot['Source'] == source)]

            if 'Derivative' in metric_df.columns:
                fig.add_trace(go.Scatter(
                    x=metric_df['TimeDelta'],
                    y=metric_df['Derivative'],
                    mode='lines',
                    name=f"{metric} производная ({source})"
                ))

    yaxis_type = 'log' if log_scale and 1 in log_scale else 'linear'
    fig.update_layout(
        template="plotly_dark",
        title="Производные метрик",
        xaxis_title="Время (сек)",
        yaxis_title="Значение производной",
        yaxis_type=yaxis_type,
        hovermode="x unified"
    )
    return fig

# Обновление статистики

@app.callback(
    Output('stats-output', 'children'),
    [Input('processed-data', 'data'),
     State('metric-selector', 'value')]
)
def update_stats_output(data, selected_metrics):
    if not data or not selected_metrics:
        return "Выберите метрики для отображения статистики"

    df = pd.DataFrame(data)
    if df.empty:
        return "Нет данных для отображения"

    stats = []
    for metric in selected_metrics:
        for source in df['Source'].unique():
            metric_df = df[(df['Metric'] == metric) & (df['Source'] == source)]
            if metric_df.empty:
                continue

            stats.append(html.H5(f"{metric} ({source})", className="text-light"))
            stats.append(html.P(f"Среднее значение: {metric_df['Value'].mean():.2f}", className="text-light"))
            stats.append(html.P(f"Мин/макс значение: {metric_df['Value'].min():.2f} / {metric_df['Value'].max():.2f}", className="text-light"))

            deriv = metric_df['Derivative'].replace([np.inf, -np.inf], np.nan).dropna()
            if not deriv.empty:
                stats.append(html.P(f"Средняя производная: {deriv.mean():.4f}", className="text-light"))
                stats.append(html.P(f"Мин/макс производная: {deriv.min():.4f} / {deriv.max():.4f}", className="text-light"))

            stats.append(html.Hr(className="bg-light"))

    return stats

@app.callback(
    Output('snapshot-output', 'children'),
    Input('snapshot-button', 'n_clicks'),
    State('processed-data', 'data'),
    State('metric-selector', 'value'),
    State('snapshot-time', 'value')
)
def generate_snapshot(n_clicks, data, selected_metrics, time_point):
    if not data or not selected_metrics or time_point is None:
        raise PreventUpdate

    df = pd.DataFrame(data)
    df = df.sort_values('TimeDelta')
    lines = [f"Снимок на TimeDelta = {time_point:.2f} сек:\n"]

    for metric in selected_metrics:
        for source in df['Source'].unique():
            subset = df[(df['Metric'] == metric) & (df['Source'] == source)]
            if subset.empty:
                continue

            closest = subset.iloc[(subset['TimeDelta'] - time_point).abs().argsort()[:1]]

            val = closest['Value'].values[0]
            smoothed = closest['Smoothed'].values[0]
            deriv = closest['Derivative'].values[0]

            lines.append(f"{metric} ({source}):")
            lines.append(f"  Value     = {val:.4f}")
            lines.append(f"  Smoothed  = {smoothed:.4f}")
            lines.append(f"  Deriv     = {deriv:.4f}")
            lines.append("")

    return "\n".join(lines)

if __name__ == "__main__":
    app.run(debug=False, port=8050)