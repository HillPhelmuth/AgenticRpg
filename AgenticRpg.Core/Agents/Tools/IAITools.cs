using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.AI;

namespace AgenticRpg.Core.Agents.Tools;

public interface IAITools
{
    List<AITool> GetAvailableTools();
}