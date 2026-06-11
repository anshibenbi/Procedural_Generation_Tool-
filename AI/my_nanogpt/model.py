import torch
import torch.nn as nn
from torch.nn import functional as F
from dataclasses import dataclass

# ============================================================
# 配置类:把所有超参数集中管理
# ============================================================
@dataclass
class GPTConfig:
    block_size: int = 128   # 上下文长度(模型一次能看多少 token)
    vocab_size: int = 65    # 词表大小(字符级莎士比亚是 65)
    n_layer: int = 4        # Transformer Block 的层数
    n_head: int = 4         # 多头注意力的头数
    n_embd: int = 128       # 每个 token 的向量维度

# ============================================================
# 占位 Block:暂时啥也不做,只保证形状不变
# 后面我们会往里填 LayerNorm、Attention、MLP
# ============================================================
class Block(nn.Module):
    def __init__(self, config):
        super().__init__()
    def forward(self, x):
        # x: (B, T, n_embd) → (B, T, n_embd),形状不变
        return x

# ============================================================
# GPT 主类
# ============================================================
class GPT(nn.Module):
    def __init__(self, config : GPTConfig):
        super().__init__()
        self.config = config

        #token embedding 把每个id映射成n_embd维的向量
        #形状变换 ：(B, T) → (B, T, n_embd)
        self.tok_emb = nn.Embedding(config.vocab_size, config.n_embd)

        #position embedding: 给每个位置一个可学习的向量，加到token embedding上
        #因为 attention 本身位置无关，所以必须显示加位置信息
        self.pos_emb = nn.Embedding(config.block_size, config.n_embd)

        # N个 Transformer Block堆叠
        self.blocks = nn.ModuleList([Block(config) for _ in range(config.n_layer)])

        #最后一个LayerNorm
        self.ln_f = nn.LayerNorm(config.n_embd)

        #输出头：把每个位置的 n_embd 向量映射成 vocab_size 的 logits
        ## (B, T, n_embd) → (B, T, vocab_size)
        self.head = nn.Linear(config.n_embd, config.vocab_size, bias=False)
    
    def forward(self, idx, targets=None):
        #idx:(B,T) 整数 token id
        #targets: (B, T) 整数 token id，每个位置是 "下一个 token" 的目标
        B, T = idx.size()
        assert T <= self.config.block_size, \
            f"序列长度 {T} 超过 block_size {self.config.block_size}"
        
        #位置索引：[0, 1, 2, ..., T - 1]
        pos = torch.arange(0, T, dtype=torch.long, device=idx.device)