import pandas as pd
import numpy as np

df = pd.read_csv('brent_exportfilename.csv')
print(f"Total Rows: {len(df)}")
print(f"Missing values:\n{df.isnull().sum()}")
print("\nBasic description (Price columns):")
print(df[['Ask', 'Bid']].describe())

# Check for anomalies (e.g., negative prices or bid > ask)
anomalies = df[df['Bid'] > df['Ask']]
print(f"\nRecords where Bid > Ask: {len(anomalies)}")

# Check Timestamp format
print(f"\nSample Timestamp: {df['Time'].iloc[0]}")
try:
    pd.to_datetime(df['Time'])
    print("Timestamp format seems to be YYYY-MM-DD HH:MM:SS")
except:
    print("Timestamp format is non-standard.")
