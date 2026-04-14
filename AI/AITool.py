#1.定义工具
def search_web(query: str) -> str:
    return f"搜索结果：{query}相关内容..."
tools = {"search_web":search_web}

#2.构建提示词
system_prompt = """
你是一个Agent，可用工具：-search_web(query) ：
搜索网络，当你需要使用工具时，输出JSON：{"tool":"工具名","args":{"参数":"值"}} 
完成任务时，直接输出答案
"""

#3.Agent主循环
def run_agent(task: str):
    messages = [{"role":"user", "content":task}]
    for _ in range(10):
        response = llm.call(system_prompt,messages)
        if is_tool_call(response):
            tool, args = parse_tool_call(response)
            result = tools[tool](**args)
            messages.append({"role":"tool","content":result})
        else:
            return response
