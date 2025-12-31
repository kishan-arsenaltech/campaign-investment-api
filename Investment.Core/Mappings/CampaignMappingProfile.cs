using AutoMapper;
using Investment.Core.Entities;

namespace Invest.Core.Mappings;

public class CampaignMappingProfile : Profile
{
    public CampaignMappingProfile()
    {
        CreateMap<CampaignDto, Campaign>()
            .ForMember(i => i.Status, x => x.MapFrom(src => src.Status))
            .ForMember(i => i.Target, x => x.MapFrom(src => src.Target))
            .ForMember(i => i.ContactInfoAddress, x => x.MapFrom(src => src.ContactInfoAddress))
            .ForMember(i => i.Description, x => x.MapFrom(src => src.Description))
            .ForMember(i => i.MinimumInvestment, x => x.MapFrom(src => src.MinimumInvestment))
            .ForMember(i => i.Website, x => x.MapFrom(src => src.Website))
            .ForMember(i => i.ContactInfoEmailAddress, x => x.MapFrom(src => src.ContactInfoEmailAddress))
            .ForMember(i => i.InvestmentInformationalEmail, x => x.MapFrom(src => src.InvestmentInformationalEmail))
            .ForMember(i => i.ContactInfoFullName, x => x.MapFrom(src => src.ContactInfoFullName))
            .ForMember(i => i.ContactInfoPhoneNumber, x => x.MapFrom(src => src.ContactInfoPhoneNumber))
            .ForMember(i => i.Name, x => x.MapFrom(src => src.Name))
            .ForMember(i => i.Terms, x => x.MapFrom(src => src.Terms))
            .ForMember(i => i.Themes, x => x.MapFrom(src => src.Themes))
            .ForMember(i => i.InvestmentTypes, x => x.MapFrom(src => src.InvestmentTypes))
            .ForMember(i => i.SDGs, x => x.MapFrom(src => src.SDGs))
            .ForMember(i => i.ApprovedBy, x => x.MapFrom(src => src.ApprovedBy))
            .ForMember(i => i.Property, x => x.MapFrom(src => src.Property))
            .ForMember(i => i.Id, x => x.MapFrom(src => src.Id));

        CreateMap<Campaign, CampaignDto>()
            .ForMember(i => i.NetworkDescription, x => x.MapFrom(src => src.NetworkDescription))
            .ForMember(i => i.FundraisingCloseDate, x => x.MapFrom(src => src.FundraisingCloseDate))
            .ForMember(i => i.Referred, x => x.MapFrom(src => src.Referred))
            .ForMember(i => i.Status, x => x.MapFrom(src => src.Status))
            .ForMember(i => i.Target, x => x.MapFrom(src => src.Target))
            .ForMember(i => i.ContactInfoAddress, x => x.MapFrom(src => src.ContactInfoAddress))
            .ForMember(i => i.Description, x => x.MapFrom(src => src.Description))
            .ForMember(i => i.MinimumInvestment, x => x.MapFrom(src => src.MinimumInvestment))
            .ForMember(i => i.Website, x => x.MapFrom(src => src.Website))
            .ForMember(i => i.ContactInfoEmailAddress, x => x.MapFrom(src => src.ContactInfoEmailAddress))
            .ForMember(i => i.InvestmentInformationalEmail, x => x.MapFrom(src => src.InvestmentInformationalEmail))
            .ForMember(i => i.ContactInfoFullName, x => x.MapFrom(src => src.ContactInfoFullName))
            .ForMember(i => i.ContactInfoPhoneNumber, x => x.MapFrom(src => src.ContactInfoPhoneNumber))
            .ForMember(i => i.Name, x => x.MapFrom(src => src.Name))
            .ForMember(i => i.Terms, x => x.MapFrom(src => src.Terms))
            .ForMember(i => i.Themes, x => x.MapFrom(src => src.Themes))
            .ForMember(i => i.InvestmentTypes, x => x.MapFrom(src => src.InvestmentTypes))
            .ForMember(i => i.SDGs, x => x.MapFrom(src => src.SDGs))
            .ForMember(i => i.ApprovedBy, x => x.MapFrom(src => src.ApprovedBy))
            .ForMember(i => i.Property, x => x.MapFrom(src => src.Property))
            .ForMember(i => i.Id, x => x.MapFrom(src => src.Id));

        CreateMap<CampaignDto, CampaignCardDto>()
            .ForMember(i => i.Status, x => x.MapFrom(src => src.Status))
            .ForMember(i => i.Target, x => x.MapFrom(src => src.Target))
            .ForMember(i => i.Description, x => x.MapFrom(src => src.Description))
            .ForMember(i => i.Name, x => x.MapFrom(src => src.Name))
            .ForMember(i => i.Themes, x => x.MapFrom(src => src.Themes))
            .ForMember(i => i.InvestmentTypes, x => x.MapFrom(src => src.InvestmentTypes))
            .ForMember(i => i.Property, x => x.MapFrom(src => src.Property))
            .ForMember(i => i.Id, x => x.MapFrom(src => src.Id));

        CreateMap<CampaignCardDto, CampaignDto>()
            .ForMember(i => i.Status, x => x.MapFrom(src => src.Status))
            .ForMember(i => i.Target, x => x.MapFrom(src => src.Target))
            .ForMember(i => i.Description, x => x.MapFrom(src => src.Description))
            .ForMember(i => i.Name, x => x.MapFrom(src => src.Name))
            .ForMember(i => i.Property, x => x.MapFrom(src => src.Property))
            .ForMember(i => i.Themes, x => x.MapFrom(src => src.Themes))
            .ForMember(i => i.Id, x => x.MapFrom(src => src.Id));
    }
}
