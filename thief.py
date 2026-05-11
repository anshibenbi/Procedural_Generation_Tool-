import os

# 读取数据（不变）
with open('convert.txt', 'r') as f:
    data = f.read().strip()

# 核心：每次运行自动找不重复的文件名
filename = "zombie_sound.mp3"
count = 1
# 只要文件存在，就加序号，直到找到不存在的文件名
while os.path.exists(filename):
    filename = f"zombie_sound_{count}.mp3"
    count += 1

# 写入文件
with open(filename, 'wb') as f:
    f.write(bytes.fromhex(data))

print(f'保存成功：{filename}')