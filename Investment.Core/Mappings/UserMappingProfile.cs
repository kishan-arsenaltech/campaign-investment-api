using AutoMapper;
using Invest.Core.Dtos;
using Investment.Core.Dtos;
using Investment.Core.Entities;

namespace Invest.Core.Mappings;
public class UserMappingProfile : Profile
{
    public UserMappingProfile()
    {
        CreateMap<UserRegistrationDto, User>();

        CreateMap<User, UserDetailsDto>()
                    .ForMember(i => i.Email, x => x.MapFrom(src => src.Email))
                    .ForMember(i => i.FirstName, x => x.MapFrom(src => src.FirstName))
                    .ForMember(i => i.LastName, x => x.MapFrom(src => src.LastName))
                    .ForMember(i => i.UserName, x => x.MapFrom(src => src.UserName))
                    .ForMember(i => i.Address, x => x.MapFrom(src => src.Address))
                    .ForMember(i => i.AccountBalance, x => x.MapFrom(src => src.AccountBalance))
                    .ForMember(i => i.PictureFileName, x => x.MapFrom(src => src.PictureFileName))
                    .ForMember(i => i.IsApprouveRequired, x => x.MapFrom(src => src.IsApprouveRequired))
                    .ForMember(i => i.IsUserHidden, x => x.MapFrom(src => src.IsUserHidden))
                    .ForMember(i => i.EmailFromGroupsOn, x => x.MapFrom(src => src.EmailFromGroupsOn))
                    .ForMember(i => i.EmailFromUsersOn, x => x.MapFrom(src => src.EmailFromUsersOn))
                    .ForMember(i => i.OptOutEmailNotifications, x => x.MapFrom(src => src.OptOutEmailNotifications))
                    .ForMember(i => i.IsFreeUser, x => x.MapFrom(src => src.IsFreeUser))
                    .ForMember(i => i.IsAnonymousInvestment, x => x.MapFrom(src => src.IsAnonymousInvestment));
    }
}