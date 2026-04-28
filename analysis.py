import pandas as pd
import numpy as np
from sklearn.linear_model import LogisticRegression
from sklearn.preprocessing import StandardScaler
from sklearn.metrics import roc_auc_score, precision_score
import warnings
warnings.filterwarnings('ignore')

# 1) Carica CSV
df = pd.read_csv('brent_exportfilename.csv', parse_dates=['Time'])
df = df.sort_values('Time').reset_index(drop=True)

# 2) Mid e Spread
df['mid'] = (df['Ask'] + df['Bid']) / 2
df['spread'] = df['Ask'] - df['Bid']

# 3) Orizzonti Temporali
horizons = [1, 2, 3, 5, 10]
for M in horizons:
    df_future = df[['Time', 'mid']].copy()
    df_future['Time'] = df_future['Time'] - pd.Timedelta(minutes=M)
    merged = pd.merge_asof(df, df_future, on='Time', direction='forward', suffixes=('', '_future'))
    df[f'ret_{M}'] = merged['mid_future'] - df['mid']
    
    p95 = df[f'ret_{M}'].quantile(0.95)
    p5 = df[f'ret_{M}'].quantile(0.05)
    df[f'LONG_{M}'] = (df[f'ret_{M}'] >= p95).astype(int)
    df[f'SHORT_{M}'] = (df[f'ret_{M}'] <= p5).astype(int)

# 4) Feature pre-evento
windows = [10, 30, 60] # secondi
df['diff_mid'] = df['mid'].diff()

def get_features(df, window_sec):
    temp = df.set_index('Time')
    suffix = f'_{window_sec}s'
    
    res = pd.DataFrame(index=df.index)
    
    # Tick rate
    res[f'tick_rate{suffix}'] = temp['mid'].rolling(f'{window_sec}s').count().values / window_sec
    
    # Imbalance
    imb = (temp['diff_mid'] > 0).astype(int) - (temp['diff_mid'] < 0).astype(int)
    res[f'imbalance{suffix}'] = imb.rolling(f'{window_sec}s').mean().values
    
    # Volatility
    res[f'vol{suffix}'] = temp['diff_mid'].rolling(f'{window_sec}s').std().values
    
    # Range
    res[f'range{suffix}'] = (temp['mid'].rolling(f'{window_sec}s').max() - temp['mid'].rolling(f'{window_sec}s').min()).values
    
    # Spread mean
    res[f'spread_mean{suffix}'] = temp['spread'].rolling(f'{window_sec}s').mean().values

    # Drift
    def drift_func(x):
        return x[-1] - x[0] if len(x) > 0 else 0
    res[f'drift{suffix}'] = temp['mid'].rolling(f'{window_sec}s').apply(drift_func, raw=True).values
    
    return res

feat_dfs = [get_features(df, w) for w in windows]
features_df = pd.concat(feat_dfs, axis=1)
df = pd.concat([df, features_df], axis=1).dropna()

feature_cols = [c for c in df.columns if any(x in c for x in ['imbalance_', 'vol_', 'range_', 'spread_mean_', 'tick_rate_', 'drift_'])]

# 5) Analisi Stats e Cohen d
def cohen_d(x, y):
    nx = len(x); ny = len(y)
    if nx < 2 or ny < 2: return 0
    pool_std = np.sqrt(((nx-1)*np.std(x, ddof=1)**2 + (ny-1)*np.std(y, ddof=1)**2) / (nx + ny - 2))
    return (np.mean(x) - np.mean(y)) / pool_std if pool_std != 0 else 0

stats_results = []
for M in [3, 5, 10]:
    for side in ['LONG', 'SHORT']:
        target = f'{side}_{M}'
        for feat in feature_cols:
            pos = df[df[target] == 1][feat]
            neg = df[df[target] == 0][feat]
            if len(pos) > 10:
                d = cohen_d(pos, neg)
                stats_results.append({'M': M, 'side': side, 'feat': feat, 'd': d, 'mean_pos': pos.mean(), 'mean_neg': neg.mean()})

# 6) Modelli Logistici
model_perf = []
for M in [3, 5, 10]:
    for side in ['LONG', 'SHORT']:
        target = f'{side}_{M}'
        split_idx = int(len(df) * 0.7)
        train_df = df.iloc[:split_idx]
        test_df = df.iloc[split_idx:]
        
        X_train, y_train = train_df[feature_cols], train_df[target]
        X_test, y_test = test_df[feature_cols], test_df[target]
        
        scaler = StandardScaler()
        X_train_s = scaler.fit_transform(X_train)
        X_test_s = scaler.transform(X_test)
        
        model = LogisticRegression(class_weight='balanced', max_iter=200).fit(X_train_s, y_train)
        probs = model.predict_proba(X_test_s)[:, 1]
        
        auc = roc_auc_score(y_test, probs)
        t5 = np.percentile(probs, 95); t10 = np.percentile(probs, 90)
        p5 = precision_score(y_test, (probs >= t5)); p10 = precision_score(y_test, (probs >= t10))
        
        model_perf.append({'M': M, 'S': side, 'AUC': auc, 'P@5': p5, 'P@10': p10})

# 7) Esaurimento (M=5) - Semplificato
print("--- PERFORMANCE MODELLI ---")
print(pd.DataFrame(model_perf).to_string())
print("\n--- TOP POSITIVE COHEN D (Predictors) ---")
print(pd.DataFrame(stats_results).sort_values('d', ascending=False).head(10).to_string())
print("\n--- TOP NEGATIVE COHEN D (Predictors) ---")
print(pd.DataFrame(stats_results).sort_values('d', ascending=True).head(10).to_string())

