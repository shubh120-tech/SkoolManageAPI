using System;

namespace SchoolManagement.Application.Communication.Dtos;

public class AnnouncementRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public bool ForStudents { get; set; } = true;
    public bool ForStaff { get; set; } = true;
}

public class AnnouncementItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public bool ForStudents { get; set; }
    public bool ForStaff { get; set; }
}

