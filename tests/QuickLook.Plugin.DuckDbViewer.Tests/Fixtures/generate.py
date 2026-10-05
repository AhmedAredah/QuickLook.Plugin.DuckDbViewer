"""Regenerates the Avro and Arrow sample files in this folder.

They are written by pyarrow and fastavro rather than by DuckDB, so the tests read files
produced by an independent implementation:

    uv run --no-project --with pyarrow --with fastavro --with cramjam python generate.py .

Every file holds the same 250 rows: id 0..249, city cycling Austin/Houston/NULL/Dallas,
score NULL for every fifth row and id * 1.5 otherwise, and a two-element integer list.
The Arrow files additionally have a "seen" timestamp column.
"""
import datetime, decimal, pyarrow as pa, pyarrow.feather as feather, pyarrow.ipc as ipc, fastavro, sys, os
out = sys.argv[1]
rows = 250
table = pa.table({
    "id": pa.array(range(rows), pa.int64()),
    "city": pa.array([["Austin", "Houston", None, "Dallas"][i % 4] for i in range(rows)], pa.string()),
    "score": pa.array([None if i % 5 == 0 else i * 1.5 for i in range(rows)], pa.float64()),
    "seen": pa.array([datetime.datetime(2025, 1, 1) + datetime.timedelta(minutes=i) for i in range(rows)], pa.timestamp("us")),
    "tags": pa.array([[i % 3, i % 7] for i in range(rows)], pa.list_(pa.int32())),
})
feather.write_feather(table, os.path.join(out, "plain.feather"), compression="uncompressed")
feather.write_feather(table, os.path.join(out, "lz4.feather"), compression="lz4")
feather.write_feather(table, os.path.join(out, "zstd.feather"), compression="zstd")
with ipc.new_file(os.path.join(out, "file.arrow"), table.schema) as w: w.write_table(table)
with ipc.new_stream(os.path.join(out, "stream.arrows"), table.schema) as w: w.write_table(table, max_chunksize=100)
schema = {"type": "record", "name": "Visit", "fields": [
    {"name": "id", "type": "long"},
    {"name": "city", "type": ["null", "string"]},
    {"name": "score", "type": ["null", "double"]},
    {"name": "tags", "type": {"type": "array", "items": "int"}},
]}
records = [{"id": i, "city": ["Austin", "Houston", None, "Dallas"][i % 4], "score": None if i % 5 == 0 else i * 1.5, "tags": [i % 3, i % 7]} for i in range(rows)]
for codec in ("null", "deflate", "snappy"):
    try:
        with open(os.path.join(out, f"{codec}.avro"), "wb") as f: fastavro.writer(f, schema, records, codec=codec)
    except Exception as e: print("avro", codec, "skipped:", e)
print("ok", pa.__version__, fastavro.__version__)
