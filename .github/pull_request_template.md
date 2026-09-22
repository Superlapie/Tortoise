## Summary

<!-- What changed and why -->

## Safety checklist

- [ ] No new privileged capability
- [ ] No arbitrary command execution
- [ ] No security feature disabled
- [ ] Tests include refusal cases where relevant
- [ ] Source provenance documented
- [ ] Safety documentation updated if behavior changed

## Test plan

- [ ] `dotnet build Tortoise.slnx -c Release`
- [ ] `dotnet test Tortoise.slnx -c Release`
