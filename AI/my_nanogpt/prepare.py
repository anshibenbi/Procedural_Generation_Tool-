import numpy as np
import pickle

# 1. 读取原始文本
with open('input.txt','r',encoding='utf-8') as f:
    text = f.read()
print(f"文本长度 : {len(text):,} 字符")

# 2. 构造字符级词表
chars = sorted(list(set(text)))
vocab_size = len(chars)
print(f"词表大小：{vocab_size}")
print(f"所有字符：{''.join(chars)}")

# 字符 <-> id的映射
stoi = {ch: i for i, ch in enumerate(chars)}
itos = {i: ch for i, ch in enumerate(chars)}

# 3. 把整个文本编码成id序列
data = np.array([stoi[c] for c in text], dtype=np.uint16)

# 4. 90%训练 / 10%验证
n = int(0.9 * len(data))
train_data = data[:n]
val_data = data[n:]
print(f"训练集：{len(train_data):,} tokens")
print(f"验证集：{len(val_data):,} tokens")

# 5. 保存为二进制文件（后面训练时用memmap高效读取）
train_data.tofile('train.bin')
val_data.tofile('val.bin')

# 6. 保存词表(生成文本时需要itos 把 id 转回字符)
with open('meta.pkl', 'wb') as f:
    pickle.dump({'vocab_size' : vocab_size, 'stoi' : stoi, 'itos' : itos}, f)

print("√ 数据准备完成")